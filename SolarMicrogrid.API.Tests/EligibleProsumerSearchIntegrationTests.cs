/*
 * EligibleProsumerSearchIntegrationTests.cs
 * -----------------------------------------------------------------------------
 * Purpose : Verifies the staff reservation picker uses a bounded MongoDB query
 *           that returns only matching active Prosumers with stable paging.
 * -----------------------------------------------------------------------------
 */

using SolarMicrogrid.API.Models.DTOs.Users;
using SolarMicrogrid.API.Services;
using SolarMicrogrid.API.Tests.Infrastructure;
using Xunit;

namespace SolarMicrogrid.API.Tests;

[Collection(Component3MongoCollection.Name)]
public sealed class EligibleProsumerSearchIntegrationTests : IAsyncLifetime
{
    private readonly Component3MongoFixture _fixture;
    private UserService _users = null!;

    public EligibleProsumerSearchIntegrationTests(Component3MongoFixture fixture)
    {
        // Reuse the isolated real MongoDB database and production user service.
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        // Restore the known user set before each server-side search assertion.
        await _fixture.ResetAsync();
        _users = new UserService(_fixture.Context);
    }

    public Task DisposeAsync()
    {
        // The shared fixture resets between facts and drops its database after the collection.
        return Task.CompletedTask;
    }

    [Fact]
    public async Task SearchReturnsOnlyActiveProsumersWithStablePaging()
    {
        // Search a shared display-name prefix and request one item per page.
        PagedEligibleProsumerResponseDto firstPage =
            await _users.SearchEligibleProsumersAsync(
                new EligibleProsumerListQueryDto
                {
                    Search = "Test Prosumer",
                    Page = 1,
                    PageSize = 1
                },
                CancellationToken.None);
        PagedEligibleProsumerResponseDto secondPage =
            await _users.SearchEligibleProsumersAsync(
                new EligibleProsumerListQueryDto
                {
                    Search = "Test Prosumer",
                    Page = 2,
                    PageSize = 1
                },
                CancellationToken.None);

        Assert.Equal(2, firstPage.TotalCount);
        Assert.Equal(2, firstPage.TotalPages);
        Assert.Single(firstPage.Items);
        Assert.Single(secondPage.Items);
        Assert.Equal(Component3MongoFixture.ProsumerOneNic, firstPage.Items[0].Nic);
        Assert.Equal(Component3MongoFixture.ProsumerTwoNic, secondPage.Items[0].Nic);
    }

    [Fact]
    public async Task SearchDoesNotReturnInactiveProsumerOrStaffAccounts()
    {
        // Match exact seeded identifiers and prove eligibility is applied before results return.
        PagedEligibleProsumerResponseDto inactive =
            await _users.SearchEligibleProsumersAsync(
                new EligibleProsumerListQueryDto
                {
                    Search = Component3MongoFixture.InactiveProsumerNic
                },
                CancellationToken.None);
        PagedEligibleProsumerResponseDto staff =
            await _users.SearchEligibleProsumersAsync(
                new EligibleProsumerListQueryDto
                {
                    Search = Component3MongoFixture.BackofficeNic
                },
                CancellationToken.None);

        Assert.Empty(inactive.Items);
        Assert.Empty(staff.Items);
    }

    [Fact]
    public async Task SearchRequiresMeaningfulTerm()
    {
        // Prevent an empty lookup from becoming an accidental full-user download.
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _users.SearchEligibleProsumersAsync(
                new EligibleProsumerListQueryDto { Search = "x" },
                CancellationToken.None));
    }
}
