using DHSIntegrationAgent.Application.Dashboard;
using DHSIntegrationAgent.Application.Persistence;
using DHSIntegrationAgent.Application.Persistence.Repositories;
using FluentAssertions;
using Moq;

namespace DHSIntegrationAgent.Tests.Unit.Application;

public class DashboardServiceTests
{
    [Fact]
    public async Task GetMetricsAsync_ShouldReturnCorrectMetrics_WhenRepositoriesReturnData()
    {
        // Arrange
        var uowFactoryMock = new Mock<ISqliteUnitOfWorkFactory>();
        var uowMock = new Mock<ISqliteUnitOfWork>();
        var claimRepoMock = new Mock<IClaimRepository>();
        var apiCallLogRepoMock = new Mock<IApiCallLogRepository>();

        uowFactoryMock.Setup(f => f.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(uowMock.Object);

        uowMock.Setup(u => u.Claims).Returns(claimRepoMock.Object);
        uowMock.Setup(u => u.ApiCallLogs).Returns(apiCallLogRepoMock.Object);

        claimRepoMock.Setup(r => r.GetDashboardCountsAsync("provider1", "payer1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((10, 5, 20, 2));

        var now = DateTimeOffset.UtcNow;

        apiCallLogRepoMock.Setup(r => r.GetLastSuccessfulCallUtcAsync("Batch_Create", It.IsAny<CancellationToken>()))
            .ReturnsAsync(now.AddMinutes(-10));

        apiCallLogRepoMock.Setup(r => r.GetLastSuccessfulCallUtcAsync("Claims_Send", It.IsAny<CancellationToken>()))
            .ReturnsAsync(now.AddMinutes(-5));

        var service = new DashboardService(uowFactoryMock.Object);

        // Act
        var result = await service.GetMetricsAsync("provider1", "payer1", CancellationToken.None);

        // Assert
        result.StagedCount.Should().Be(10);
        result.EnqueuedCount.Should().Be(5);
        result.CompletedCount.Should().Be(20);
        result.FailedCount.Should().Be(2);

        result.LastSendUtc.Should().Be(now.AddMinutes(-5));
        result.LastFetchUtc.Should().Be(now.AddMinutes(-10));
    }
}
