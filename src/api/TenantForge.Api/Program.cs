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

// Authentication and authorization run in every environment. The JWT scheme is
// registered by the IAM module unconditionally, but outside Development its
// signing key does not exist (configuration validation forbids it), so token
// validation always fails and protected endpoints answer 401 — fail closed.
app.UseAuthentication();
app.UseAuthorization();

app.MapHealth();
app.MapIamModule();

app.Run();

public partial class Program;
