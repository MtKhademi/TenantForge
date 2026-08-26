using TenantForge.Api;
using TenantForge.Modules.Iam;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            policy.WithOrigins(builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? Array.Empty<string>())
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
    });
});

builder.Services.AddIamModule(builder.Environment);

var app = builder.Build();

IamModule.ValidateIamModuleConfiguration(builder.Environment, app.Configuration);

app.UseCors();

app.MapHealth();
app.MapIamModule();

app.Run();

public partial class Program;
