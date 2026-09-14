using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TenantForge.Modules.Iam;
using Xunit;

namespace TenantForge.Api.IntegrationTests;

/// <summary>
/// Protects the IAM module's public composition seam introduced by B016: the
/// host must be able to compose IAM through exactly two static calls
/// (registration before <c>Build</c>, asynchronous activation after
/// <c>Build</c>), and nothing else IAM-owned may be reachable from outside the
/// module.
/// </summary>
public sealed class IamModuleCompositionSurfaceTests
{
    [Fact]
    public void IamModule_ExposesExactlyTwoPublicStaticMethods_WithTheExpectedSignatures()
    {
        var publicStaticMethods = typeof(IamModule)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            // Exclude the small set of methods every static class implicitly
            // carries (e.g. those inherited from System.Object are not static,
            // but extension methods compiled from `this` parameters show up
            // here as ordinary static methods, which is exactly what we want
            // to enumerate).
            .Where(method => !method.IsSpecialName)
            .ToArray();

        var methodNames = publicStaticMethods.Select(method => method.Name).OrderBy(name => name).ToArray();
        Assert.Equal(new[] { "AddIamModule", "UseIamModuleAsync" }, methodNames);

        var addIamModule = publicStaticMethods.Single(method => method.Name == "AddIamModule");
        var addParameters = addIamModule.GetParameters();
        Assert.Equal(2, addParameters.Length);
        Assert.Equal(typeof(IServiceCollection), addParameters[0].ParameterType);
        Assert.Equal(typeof(IHostEnvironment), addParameters[1].ParameterType);
        Assert.Equal(typeof(IServiceCollection), addIamModule.ReturnType);

        var useIamModuleAsync = publicStaticMethods.Single(method => method.Name == "UseIamModuleAsync");
        var useParameters = useIamModuleAsync.GetParameters();
        Assert.Single(useParameters);
        Assert.Equal(typeof(WebApplication), useParameters[0].ParameterType);
        // Activation is genuinely asynchronous: a synchronous
        // "UseIamModuleAsync" that secretly blocks would be a regression the
        // Spec explicitly forbids, so the return type itself must be awaitable.
        Assert.Equal(typeof(Task), useIamModuleAsync.ReturnType);
    }

    [Fact]
    public void FormerlySeparateHostCalls_AreNoLongerPublic()
    {
        var publicStaticMethodNames = typeof(IamModule)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(method => method.Name)
            .ToArray();

        Assert.DoesNotContain("ValidateIamModuleConfiguration", publicStaticMethodNames);
        Assert.DoesNotContain("SeedIamModuleAsync", publicStaticMethodNames);
        Assert.DoesNotContain("MapIamModule", publicStaticMethodNames);
    }
}

/// <summary>
/// Exercises the real host through the new two-phase seam: fresh startup must
/// migrate and seed before the first login can succeed, and a second,
/// independent host built against the same database must not duplicate or
/// otherwise disturb the platform administrator (activation's migrate/seed
/// step is idempotent, matching the pre-B016 behavior).
/// </summary>
[Collection(nameof(CompositionSeamIsolatedCollection))]
public sealed class IamModuleActivationIntegrationTests(CompositionSeamDbFixture db)
{
    [Fact]
    public async Task FreshStartup_MigratesAndSeeds_BeforeFirstLoginSucceeds()
    {
        using var factory = new ApiFactory(environment: "Development", seedMode: IamSeedMode.Complete, db);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = ApiFactory.Email,
            password = ApiFactory.Password
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RepeatStartup_AgainstTheSameDatabase_DoesNotDuplicateTheAdministrator()
    {
        using (var firstHost = new ApiFactory(environment: "Development", seedMode: IamSeedMode.Complete, db))
        {
            using var firstClient = firstHost.CreateClient();
            var firstLogin = await firstClient.PostAsJsonAsync("/api/auth/login", new
            {
                email = ApiFactory.Email,
                password = ApiFactory.Password
            });
            Assert.Equal(HttpStatusCode.OK, firstLogin.StatusCode);
        }

        // A second, independent host activates against the same database,
        // simulating a process restart. Activation's migrate-then-seed step
        // must run again without error and without creating a duplicate
        // administrator.
        using var secondHost = new ApiFactory(environment: "Development", seedMode: IamSeedMode.Complete, db);
        using var secondClient = secondHost.CreateClient();

        var secondLogin = await secondClient.PostAsJsonAsync("/api/auth/login", new
        {
            email = ApiFactory.Email,
            password = ApiFactory.Password
        });

        Assert.Equal(HttpStatusCode.OK, secondLogin.StatusCode);

        using var db2 = db.CreateContext();
        Assert.Equal(1, await db2.Accounts.CountAsync());
    }
}
