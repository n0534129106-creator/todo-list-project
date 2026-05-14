using Microsoft.EntityFrameworkCore;
using TodoApi;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// 1. שליפת משתני סביבה בצורה בטוחה
var host = Environment.GetEnvironmentVariable("DB_HOST");
var port = Environment.GetEnvironmentVariable("DB_PORT") ?? "3306";
var database = Environment.GetEnvironmentVariable("DB_NAME");
var user = Environment.GetEnvironmentVariable("DB_USER");
var password = Environment.GetEnvironmentVariable("DB_PASSWORD");

string connectionString;

// בדיקה האם אנחנו ב-Production (Render) או ב-Local
if (!string.IsNullOrEmpty(host))
{
    // שימוש ב-User במקום User Id כדי למנוע את השגיאה Option 'name' not supported
    connectionString = $"Server={host};Port={port};Database={database};User={user};Password={password};SSL Mode=Required;";
    Console.WriteLine("Environment: Production (Render)");
}
else
{
    connectionString = builder.Configuration.GetConnectionString("ToDoDB") ?? "";
    Console.WriteLine("Environment: Local");
}

// 2. הגדרת מסד הנתונים
var serverVersion = new MySqlServerVersion(new Version(8, 0, 36)); 

builder.Services.AddDbContext<ToDoDbContext>(options =>
    options.UseMySql(connectionString, serverVersion, mysqlOptions => 
    {
        // הוספת Retries למקרה של חיבור איטי ב-Cloud
        mysqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(30),
            errorNumbersToAdd: null);
    }));

// 3. שירותים בסיסיים ו-CORS
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

// 4. הגדרת JWT
var jwtKey = Environment.GetEnvironmentVariable("JWT_KEY") ?? "ThisIsMyVerySecretKeyForJwt1234567890";
var key = Encoding.ASCII.GetBytes(jwtKey);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(key),
            ValidateIssuer = false,
            ValidateAudience = false
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// 5. יצירת טבלאות אוטומטית (עם טיפול בשגיאות)
try 
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ToDoDbContext>();
    db.Database.EnsureCreated();
}
catch (Exception ex)
{
    Console.WriteLine($"Database initialization failed: {ex.Message}");
}

// 6. Middleware
app.UseSwagger();
app.UseSwaggerUI();
app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();

// --- Routes ---

app.MapGet("/", () => "Server is running!");

app.MapPost("/register", async (ToDoDbContext db, User user) =>
{
    try 
    {
        var existingUser = await db.Users.FirstOrDefaultAsync(u => u.Username == user.Username);
        if (existingUser != null)
            return Results.BadRequest("משתמש זה כבר קיים במערכת");

        db.Users.Add(user);
        await db.SaveChangesAsync();
        return Results.Ok(new { message = "User registered successfully" });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Database Error: {ex.Message}");
    }
});

// שאר ה-Routes שלך (login, items וכו') יבואו כאן...

app.Run();