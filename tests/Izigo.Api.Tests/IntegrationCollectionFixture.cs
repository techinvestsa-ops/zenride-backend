using Xunit;

namespace Izigo.Api.Tests;

/// <summary>
/// Shared collection fixture — all integration tests that share the same
/// WebApplicationFactory instance must declare [Collection("Integration")].
/// This means the factory (and therefore the DB) is created once per test run,
/// while each test class resets the DB via Respawn in InitializeAsync().
/// </summary>
[CollectionDefinition("Integration")]
public class IntegrationCollection : ICollectionFixture<IzigoWebApplicationFactory> { }
