using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Moq;
using SolarMicrogrid.API.Controllers;
using SolarMicrogrid.API.Exceptions;
using SolarMicrogrid.API.Middleware;
using SolarMicrogrid.API.Models.DTOs.Stations;
using SolarMicrogrid.API.Models.Entities;
using SolarMicrogrid.API.Services;
using Xunit;

namespace SolarMicrogrid.Tests;

public sealed class Stage18AuditTests
{
    [Theory]
    [InlineData(ReservationStatus.Cancelled)]
    [InlineData(ReservationStatus.Completed)]
    public async Task HistoricalOnlyReservations_DoNotBlockStationDeactivation(
        ReservationStatus historicalStatus)
    {
        var database = new MongoTestContext();
        SolarStationInfo station = StationServiceTests.Station();
        database.ReturnStations(station);
        FilterDefinition<EnergyReservation>? reservationFilter = null;
        database.Reservations
            .Setup(collection => collection.FindAsync<EnergyReservation>(
                It.IsAny<FilterDefinition<EnergyReservation>>(),
                It.IsAny<FindOptions<EnergyReservation, EnergyReservation>>(),
                It.IsAny<CancellationToken>()))
            .Callback<FilterDefinition<EnergyReservation>,
                FindOptions<EnergyReservation, EnergyReservation>, CancellationToken>(
                (filter, _, _) => reservationFilter = filter)
            .ReturnsAsync(() => MongoTestContext.Cursor<EnergyReservation>([]));
        database.Stations
            .Setup(collection => collection.FindOneAndUpdateAsync<SolarStationInfo>(
                It.IsAny<FilterDefinition<SolarStationInfo>>(),
                It.IsAny<UpdateDefinition<SolarStationInfo>>(),
                It.IsAny<FindOneAndUpdateOptions<SolarStationInfo, SolarStationInfo>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                station.IsActive = false;
                return station;
            });

        var service = new StationService(database.Context, new ReservationGuardService(database.Context));
        var result = await service.DeactivateStationAsync(station.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.False(result.IsActive);
        Assert.NotNull(reservationFilter);
        string rendered = reservationFilter.Render(new RenderArgs<EnergyReservation>(
            BsonSerializer.LookupSerializer<EnergyReservation>(), BsonSerializer.SerializerRegistry)).ToJson();
        Assert.Contains("Pending", rendered);
        Assert.Contains("Approved", rendered);
        Assert.DoesNotContain(historicalStatus.ToString(), rendered);
    }

    [Fact]
    public async Task MissingStation_ReturnsNotFoundFromBothDeactivationRoutes()
    {
        var database = new MongoTestContext();
        database.ReturnStations();
        var controller = new StationsController(new StationService(
            database.Context, new ReservationGuardService(database.Context)));
        string missingId = ObjectId.GenerateNewId().ToString();

        ActionResult<StationResponseDto> patchResult =
            await controller.DeactivateStation(missingId, CancellationToken.None);
        IActionResult deleteResult = await controller.DeleteStation(missingId, CancellationToken.None);

        Assert.IsType<NotFoundResult>(patchResult.Result);
        Assert.IsType<NotFoundResult>(deleteResult);
    }

    [Fact]
    public async Task MissingSlot_ReturnsNotFoundFromDeleteRoute()
    {
        var database = new MongoTestContext();
        database.ReturnSlots();
        var slotService = new SlotService(database.Context);
        var controller = new SlotsController(
            slotService, new StationAccessService(database.Context, slotService));

        IActionResult result = await controller.DeleteSlot(
            ObjectId.GenerateNewId().ToString(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Theory]
    [InlineData(typeof(SlotsController), nameof(SlotsController.DeleteSlot), UserRole.GridOperator, true)]
    [InlineData(typeof(SlotsController), nameof(SlotsController.DeleteSlot), UserRole.Prosumer, true)]
    [InlineData(typeof(SlotsController), nameof(SlotsController.DeleteSlot), UserRole.Backoffice, false)]
    [InlineData(typeof(StationsController), nameof(StationsController.DeleteStation), UserRole.GridOperator, true)]
    [InlineData(typeof(StationsController), nameof(StationsController.DeleteStation), UserRole.Prosumer, true)]
    [InlineData(typeof(StationsController), nameof(StationsController.DeleteStation), UserRole.Backoffice, false)]
    public async Task DeleteRoutePolicy_ForbidsNonBackofficeUsers(
        Type controllerType,
        string actionName,
        UserRole role,
        bool expectForbidden)
    {
        var authorization = controllerType.GetMethod(actionName)!
            .GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorization);
        AuthorizationPolicy policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireRole(authorization.Roles!.Split(','))
            .Build();
        using ServiceProvider services = new ServiceCollection()
            .AddLogging()
            .AddAuthorization()
            .BuildServiceProvider();
        var evaluator = new PolicyEvaluator(services.GetRequiredService<IAuthorizationService>());
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, role.ToString())], "test"));
        var context = new DefaultHttpContext { User = principal };
        AuthenticateResult authenticated = AuthenticateResult.Success(
            new AuthenticationTicket(principal, "test"));

        PolicyAuthorizationResult result = await evaluator.AuthorizeAsync(
            policy, authenticated, context, resource: null);

        Assert.Equal(expectForbidden, result.Forbidden);
        Assert.Equal(!expectForbidden, result.Succeeded);
    }

    [Theory]
    [InlineData(typeof(ForbiddenException), StatusCodes.Status403Forbidden)]
    [InlineData(typeof(ConflictException), StatusCodes.Status409Conflict)]
    [InlineData(typeof(NotFoundException), StatusCodes.Status404NotFound)]
    public async Task DomainFailures_MapToExpectedHttpStatus(Type exceptionType, int expectedStatus)
    {
        Exception exception = (Exception)Activator.CreateInstance(exceptionType, "test failure")!;
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new ExceptionMiddleware(
            _ => throw exception,
            NullLogger<ExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(expectedStatus, context.Response.StatusCode);
    }

    [Fact]
    public async Task CreatedStation_SerializesGeoJsonCoordinatesLongitudeFirst()
    {
        var database = new MongoTestContext();
        SolarStationInfo? inserted = null;
        database.Stations
            .Setup(collection => collection.InsertOneAsync(
                It.IsAny<SolarStationInfo>(),
                It.IsAny<InsertOneOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<SolarStationInfo, InsertOneOptions, CancellationToken>(
                (station, _, _) => inserted = station)
            .Returns(Task.CompletedTask);
        var request = StationServiceTests.ValidCreateRequest();
        request.Latitude = 6.9271;
        request.Longitude = 79.8612;

        await new StationService(database.Context, new ReservationGuardService(database.Context))
            .CreateStationAsync(request, CancellationToken.None);

        Assert.NotNull(inserted);
        BsonArray coordinates = inserted.ToBsonDocument()["location"]["coordinates"].AsBsonArray;
        Assert.Equal(request.Longitude, coordinates[0].AsDouble);
        Assert.Equal(request.Latitude, coordinates[1].AsDouble);
    }

    [Fact]
    public async Task Deactivation_ForwardsCancellationTokenToEveryMongoOperation()
    {
        var database = new MongoTestContext();
        SolarStationInfo station = StationServiceTests.Station();
        database.ReturnStations(station);
        database.ReturnReservations();
        database.Stations
            .Setup(collection => collection.FindOneAndUpdateAsync<SolarStationInfo>(
                It.IsAny<FilterDefinition<SolarStationInfo>>(),
                It.IsAny<UpdateDefinition<SolarStationInfo>>(),
                It.IsAny<FindOneAndUpdateOptions<SolarStationInfo, SolarStationInfo>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                station.IsActive = false;
                return station;
            });
        using var cancellationSource = new CancellationTokenSource();
        CancellationToken token = cancellationSource.Token;

        await new StationService(database.Context, new ReservationGuardService(database.Context))
            .DeactivateStationAsync(station.Id, token);

        database.Stations.Verify(collection => collection.FindAsync<SolarStationInfo>(
            It.IsAny<FilterDefinition<SolarStationInfo>>(),
            It.IsAny<FindOptions<SolarStationInfo, SolarStationInfo>>(), token), Times.Once);
        database.Reservations.Verify(collection => collection.FindAsync<EnergyReservation>(
            It.IsAny<FilterDefinition<EnergyReservation>>(),
            It.IsAny<FindOptions<EnergyReservation, EnergyReservation>>(), token), Times.Once);
        database.Stations.Verify(collection => collection.FindOneAndUpdateAsync<SolarStationInfo>(
            It.IsAny<FilterDefinition<SolarStationInfo>>(),
            It.IsAny<UpdateDefinition<SolarStationInfo>>(),
            It.IsAny<FindOneAndUpdateOptions<SolarStationInfo, SolarStationInfo>>(), token), Times.Once);
    }
}
