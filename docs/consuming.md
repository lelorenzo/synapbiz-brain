# Consuming the packages

The three packages (`Synapbiz.Brain.Contracts`, `Synapbiz.Brain.Engine`, `Synapbiz.Brain.Spec`) are published to the private GitHub Packages NuGet feed of the repository owner when a tag `vX.Y.Z` is pushed (`.github/workflows/release.yml`).

## Feed

Add a `nuget.config` next to the consumer's solution. It holds no secret; the credentials come from the environment.

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="synapbiz-brain" value="https://nuget.pkg.github.com/lelorenzo/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
    <packageSource key="synapbiz-brain">
      <package pattern="Synapbiz.Brain.*" />
    </packageSource>
  </packageSourceMapping>
  <packageSourceCredentials>
    <synapbiz-brain>
      <add key="Username" value="%NUGET_FEED_USER%" />
      <add key="ClearTextPassword" value="%NUGET_FEED_TOKEN%" />
    </synapbiz-brain>
  </packageSourceCredentials>
</configuration>
```

- In GitHub Actions of a consumer repository: grant that repository read access in each package's settings (Package settings › Manage Actions access), then set `NUGET_FEED_USER: ${{ github.actor }}` and `NUGET_FEED_TOKEN: ${{ secrets.GITHUB_TOKEN }}` with `permissions: packages: read`.
- On a developer machine: a personal access token with the `read:packages` scope in `NUGET_FEED_TOKEN`. Never commit it.

## Use

```csharp
var contract = BrainContract.Parse(File.ReadAllText("contract-module.json"));
var loaded = BrainRuleLoader.Load(File.ReadAllText("some.rule.json"), [contract]);
if (!loaded.IsValid) { /* loaded.Errors: code, path, EN/ES message, fix hint */ }

var result = loaded.Rule!.Evaluate(facts, asOf);
// result.Outcome: Decided (OutcomeJson, MatchedRowKeys) or NoDecision
// result.Trace:   VersionNumber, MatchedRowKeys, FactsJson, FactsHash
```

The document and contract formats are in [reference/brain-rule-v1.md](reference/brain-rule-v1.md).

## Releasing

1. Update `CHANGELOG.md`.
2. Push a tag `vX.Y.Z` on `main`. The workflow builds, tests, packs with that version and publishes.
A published version is immutable: a fix is a new version.
