using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.OData;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using Microsoft.OpenApi.Models;
using StudentManage_API.Interfaces;
using StudentManage_API.Models;
using StudentManage_API.Services;
using System.Text;

namespace StudentManage_API
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.

            // Database Context
            builder.Services.AddDbContext<StudentManagementDbContext>(options =>
                options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

            // JWT Configuration
            var jwtSettings = builder.Configuration.GetSection("Jwt");
            var jwtKey = jwtSettings["Key"];
            var jwtIssuer = jwtSettings["Issuer"];
            var jwtAudience = jwtSettings["Audience"];

            builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        ValidIssuer = jwtIssuer,
                        ValidAudience = jwtAudience,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                        ClockSkew = TimeSpan.Zero
                    };
                });

            // Authorization Policies
            builder.Services.AddAuthorization(options =>
            {
                options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
                options.AddPolicy("TeacherOnly", policy => policy.RequireRole("Teacher"));
                options.AddPolicy("StudentOnly", policy => policy.RequireRole("Student"));
                options.AddPolicy("TeacherOrAdmin", policy => policy.RequireRole("Teacher", "Admin"));
                options.AddPolicy("AllRoles", policy => policy.RequireRole("Admin", "Teacher", "Student"));
            });

            // OData Configuration
            builder.Services.AddControllers().AddOData(options =>
                options.Select().Filter().OrderBy().Expand().Count().SetMaxTop(1000)
                       .AddRouteComponents("odata", GetEdmModel()));

            // CORS
            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowAll",
                    policy =>
                    {
                        policy.AllowAnyOrigin()
                              .AllowAnyMethod()
                              .AllowAnyHeader();
                    });
            });

            // Register Services
            builder.Services.AddScoped<IAuthService, AuthService>();

            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new() { Title = "Student Management API", Version = "v1" });

                // JWT Bearer configuration for Swagger
                c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token",
                    Name = "Authorization",
                    In = ParameterLocation.Header,
                    Type = SecuritySchemeType.ApiKey,
                    Scheme = "Bearer"
                });

                c.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference
                            {
                                Type = ReferenceType.SecurityScheme,
                                Id = "Bearer"
                            }
                        },
                        Array.Empty<string>()
                    }
                });
            });

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();

            app.UseCors("AllowAll");

            app.UseAuthentication();
            app.UseAuthorization();

            app.MapControllers();

            app.Run();
        }

        // OData Model Configuration
        private static IEdmModel GetEdmModel()
        {
            ODataConventionModelBuilder modelBuilder = new();

            modelBuilder.EntitySet<User>("Users");
            modelBuilder.EntitySet<Class>("Classes");
            modelBuilder.EntitySet<Subject>("Subjects");
            modelBuilder.EntitySet<Score>("Scores");
            modelBuilder.EntitySet<Attendance>("Attendances");
            modelBuilder.EntitySet<Schedule>("Schedules");
            modelBuilder.EntitySet<Notification>("Notifications");
            modelBuilder.EntitySet<StudentClass>("StudentClasses");
            modelBuilder.EntitySet<ClassSubject>("ClassSubjects");
            modelBuilder.EntitySet<Parent>("Parents");
            modelBuilder.EntitySet<StudentParent>("StudentParents");

            // Views
            modelBuilder.EntitySet<VwClassStatistic>("VwClassStatistics");
            modelBuilder.EntitySet<VwStudentsWithClass>("VwStudentsWithClasses");
            modelBuilder.EntitySet<VwTeachersWithSubject>("VwTeachersWithSubjects");

            return modelBuilder.GetEdmModel();
        }
    }
}