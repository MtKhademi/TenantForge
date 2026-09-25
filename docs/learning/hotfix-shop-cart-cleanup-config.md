# Hotfix — Development Shop cart cleanup configuration

## 1. Files changed and why

- `src/api/TenantForge.Api/appsettings.Development.json` now supplies `Shop:CartCleanupIntervalSeconds` with a valid value (`3600`). Shop already required this key during activation and the cart cleanup worker reads it at startup, but the real Development settings used by `dotnet.exe run` did not include it.
- `docs/knowledge/AGENT-backend.md` records the durable trap: hermetic tests can pass while the real Development settings are missing a required module key.
- `docs/knowledge/HUMAN-backend.md` explains that local startup includes the Shop cleanup interval.

## 2. Request flow from endpoint to response

No endpoint changed. The important flow is application startup:

1. The API host builds configuration from `appsettings.json`, `appsettings.Development.json`, launch settings and environment variables.
2. `Program.cs` registers Shop before `Build` with `AddShopModule`.
3. After `Build`, `UseShopModuleAsync` calls `ShopConfig.ValidateConfiguration`.
4. Validation now finds `Shop:CartCleanupIntervalSeconds` in the Development settings, so startup continues to Shop migrations and endpoint mapping.
5. `ShopCartCleanupWorker` can read the same configured value when it starts its background loop.

## 3. Backend concepts introduced

- Module activation validates fully assembled configuration after `Build`, so late sources remain visible.
- Integration-test factories are intentionally hermetic; they do not prove the repository's real `appsettings.Development.json` contains every required local-demo key.
- A configuration hotfix can preserve fail-closed validation by adding the missing Development setting instead of weakening the module's validation rule.

## 4. Important security decisions

- The fix does not relax Shop startup validation.
- Invalid or out-of-range cleanup intervals still fail startup.
- No credentials, tokens or payment secrets were added or logged.
- Production payment and rate-limit fail-closed behavior is unchanged.

## 5. Alternatives deliberately postponed

- I did not add a Development default in `ShopConfig.GetCartCleanupIntervalSeconds`; the handbook says the key is always required, and tests already cover invalid values.
- I did not add a new endpoint or health behavior.
- I did not change the cleanup worker interval bounds or cart-expiry semantics.

## 6. Commands and manual steps to verify

```bash
dotnet.exe build TenantForge.sln
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --filter FullyQualifiedName~ShopModuleIntegrationTests
timeout 20s dotnet.exe run --project src/api/TenantForge.Api/TenantForge.Api.csproj --urls http://0.0.0.0:5099
```

Expected smoke-test evidence: startup reaches `Now listening on: http://0.0.0.0:5099` after IAM and Shop migrations, instead of throwing the `Shop:CartCleanupIntervalSeconds` validation exception.

## 7. Review questions

1. Why is adding the setting to `appsettings.Development.json` safer here than making the setting optional in `ShopConfig`?
2. Why can `WebApplicationFactory` tests pass while `dotnet.exe run` fails on a missing appsettings key?
3. Which phase owns Shop configuration validation: `AddShopModule` or `UseShopModuleAsync`, and why?
