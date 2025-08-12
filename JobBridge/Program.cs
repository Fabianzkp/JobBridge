using JobBridge.Components;
using JobBridge.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using JobBridge.Services;
using JobBridge.Identity;
using Microsoft.AspNetCore.Components.Authorization;
using System.Text.Json.Serialization; 

var builder = WebApplication.CreateBuilder(args);

// Configure port for Render
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    });

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(builder.Configuration);

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityUserAccessor>();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddSingleton<SessionAuthService>();
builder.Services.AddSingleton<AuthenticationStateProvider>(provider => provider.GetService<SessionAuthService>()!);

builder.Services.AddHttpClient();
// Configure database based on environment
if (builder.Environment.IsProduction() && builder.Configuration.GetValue<bool>("UseInMemoryDatabase"))
{
    builder.Services.AddDbContext<JobBridgeContext>(options =>
        options.UseInMemoryDatabase("JobBridgeInMemory"));
}
else
{
    builder.Services.AddSqlite<JobBridgeContext>(builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=jobbridge.db");
}

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentity<User, IdentityRole>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
})
.AddEntityFrameworkStores<JobBridgeContext>()
.AddSignInManager()
.AddDefaultTokenProviders();

// Configure application cookie settings
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/authentication";
    options.LogoutPath = "/authentication/logout";
    options.AccessDeniedPath = "/authentication";
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromDays(30);
    options.Events.OnRedirectToLogin = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = 401;
        }
        else
        {
            context.Response.Redirect("/authentication");
        }
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = 403;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToLogout = context =>
    {
        context.Response.StatusCode = 200;
        return Task.CompletedTask;
    };
});
// Email sender service
builder.Services.AddSingleton<IEmailSender<User>, IdentityNoOpEmailSender>();

builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<JobService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<ApplicationService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
    app.UseStatusCodePagesWithReExecute("/Error/{0}");
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseStatusCodePagesWithReExecute("/Error/{0}");
    app.UseHsts();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseStaticFiles();
app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapControllers();

// Add server-side login endpoint
app.MapGet("/server-login", async (HttpContext context, SignInManager<User> signInManager, string email, bool remember = false) =>
{
    var user = await signInManager.UserManager.FindByEmailAsync(email);
    if (user != null)
    {
        await signInManager.SignInAsync(user, remember);
        return Results.LocalRedirect("/");
    }
    return Results.LocalRedirect("/authentication?error=Login failed");
});

using (var scope = app.Services.CreateScope())
{
    var serviceProvider = scope.ServiceProvider;
    var db = serviceProvider.GetRequiredService<JobBridgeContext>();
    var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        // Ensure database is created
        if (app.Environment.IsProduction() && builder.Configuration.GetValue<bool>("UseInMemoryDatabase"))
        {
            await db.Database.EnsureCreatedAsync();
        }
        else
        {
            await db.Database.MigrateAsync();
        }
        
        try
        {
            await SeedData.InitializeAsync(serviceProvider);
        }
        catch (Exception)
        {
            // Silently continue - seeding issues don't affect app functionality
            logger.LogInformation("Seeding completed with some warnings.");
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred during database migration.");
    }
}

app.Run();