using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DHSIntegrationAgent.Application.Abstractions;
using DHSIntegrationAgent.Application.Persistence;
using DHSIntegrationAgent.Application.Persistence.Repositories;
using DHSIntegrationAgent.Contracts.Claims;
using DHSIntegrationAgent.Contracts.Persistence;
using DHSIntegrationAgent.Domain.Claims;
using DHSIntegrationAgent.Domain.WorkStates;
using DHSIntegrationAgent.Workers;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using DHSIntegrationAgent.Contracts.Workers;

namespace DHSIntegrationAgent.Tests.Unit.Workers;

public class DispatchServiceSenderTests
{
    private readonly Mock<ISqliteUnitOfWorkFactory> _uowFactoryMock;
    private readonly Mock<IClaimsClient> _claimsClientMock;
    private readonly Mock<ISystemClock> _clockMock;
    private readonly Mock<ILogger<DispatchService>> _loggerMock;
    private readonly DispatchService _service;

    public DispatchServiceSenderTests()
    {
        _uowFactoryMock = new Mock<ISqliteUnitOfWorkFactory>();
        _claimsClientMock = new Mock<IClaimsClient>();
        _clockMock = new Mock<ISystemClock>();
        _loggerMock = new Mock<ILogger<DispatchService>>();

        _service = new DispatchService(
            _uowFactoryMock.Object,
            _claimsClientMock.Object,
            _clockMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task ProcessBatchSenderAsync_AllSuccess_ShouldMarkAllEnqueued()
    {
        // Setup
        var batch = new BatchRow(1, "provider", "company", "payer", "202401", null, null, "bcr-123", BatchStatus.Ready, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, 0, 0, "", 0);

        var uowMock = new Mock<ISqliteUnitOfWork>();
        var claimRepoMock = new Mock<IClaimRepository>();
        var claimPayloadRepoMock = new Mock<IClaimPayloadRepository>();
        var dispatchRepoMock = new Mock<IDispatchRepository>();
        var dispatchItemRepoMock = new Mock<IDispatchItemRepository>();
        var batchRepoMock = new Mock<IBatchRepository>();
        var domainMappingsRepoMock = new Mock<IDomainMappingRepository>();

        uowMock.Setup(u => u.Claims).Returns(claimRepoMock.Object);
        uowMock.Setup(u => u.ClaimPayloads).Returns(claimPayloadRepoMock.Object);
        uowMock.Setup(u => u.Dispatches).Returns(dispatchRepoMock.Object);
        uowMock.Setup(u => u.DispatchItems).Returns(dispatchItemRepoMock.Object);
        uowMock.Setup(u => u.Batches).Returns(batchRepoMock.Object);
        uowMock.Setup(u => u.DomainMappings).Returns(domainMappingsRepoMock.Object);

        _uowFactoryMock.Setup(f => f.CreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(uowMock.Object);

        // First pass: return 2 claims to lease
        var claim1 = new ClaimKey("provider", 100);
        var claim2 = new ClaimKey("provider", 101);
        var leasedClaims = new List<ClaimKey> { claim1, claim2 };

        claimRepoMock.SetupSequence(c => c.LeaseAsync(It.IsAny<ClaimLeaseRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(leasedClaims) // first call returns 2 claims
            .ReturnsAsync(new List<ClaimKey>()); // second call returns empty list to stop the loop

        claimRepoMock.Setup(c => c.GetBatchCountsAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((2, 0, 0));

        domainMappingsRepoMock.Setup(r => r.GetAllApprovedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ApprovedDomainMappingRow>());

        // Mock payload lookup
        claimPayloadRepoMock.Setup(p => p.GetAsync(It.IsAny<ClaimKey>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClaimPayloadRow(new ClaimKey("provider", 100), System.Text.Encoding.UTF8.GetBytes("{\"claimHeader\": {\"proidclaim\": 100}}"), "", 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

        // Mock API response - ALL SUCCESS
        _claimsClientMock.Setup(c => c.SendClaimAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendClaimResult(true, null, new List<long> { 100, 101 }, 200));

        var progressMock = new Mock<IProgress<WorkerProgressReport>>();

        // Act
        await _service.ProcessBatchSenderAsync(batch, progressMock.Object, CancellationToken.None);

        // Assert
        // Check that MarkEnqueuedAsync was called with both keys
        claimRepoMock.Verify(c => c.MarkEnqueuedAsync(
            It.Is<IReadOnlyList<ClaimKey>>(keys => keys.Count == 2),
            It.IsAny<string>(),
            It.IsAny<DateTimeOffset>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Ensure MarkFailedAsync was not called
        claimRepoMock.Verify(c => c.MarkFailedAsync(
            It.IsAny<IReadOnlyList<ClaimKey>>(),
            It.IsAny<string>(),
            It.IsAny<DateTimeOffset>(),
            It.IsAny<TimeSpan?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessBatchSenderAsync_PartialSuccess_ShouldMarkFailuresDeterministically()
    {
        // Setup
        var batch = new BatchRow(1, "provider", "company", "payer", "202401", null, null, "bcr-123", BatchStatus.Ready, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, 0, 0, "", 0);

        var uowMock = new Mock<ISqliteUnitOfWork>();
        var claimRepoMock = new Mock<IClaimRepository>();
        var claimPayloadRepoMock = new Mock<IClaimPayloadRepository>();
        var dispatchRepoMock = new Mock<IDispatchRepository>();
        var dispatchItemRepoMock = new Mock<IDispatchItemRepository>();
        var batchRepoMock = new Mock<IBatchRepository>();
        var domainMappingsRepoMock = new Mock<IDomainMappingRepository>();

        uowMock.Setup(u => u.Claims).Returns(claimRepoMock.Object);
        uowMock.Setup(u => u.ClaimPayloads).Returns(claimPayloadRepoMock.Object);
        uowMock.Setup(u => u.Dispatches).Returns(dispatchRepoMock.Object);
        uowMock.Setup(u => u.DispatchItems).Returns(dispatchItemRepoMock.Object);
        uowMock.Setup(u => u.Batches).Returns(batchRepoMock.Object);
        uowMock.Setup(u => u.DomainMappings).Returns(domainMappingsRepoMock.Object);

        _uowFactoryMock.Setup(f => f.CreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(uowMock.Object);

        // First pass: return 2 claims to lease
        var claim1 = new ClaimKey("provider", 100);
        var claim2 = new ClaimKey("provider", 101);
        var leasedClaims = new List<ClaimKey> { claim1, claim2 };

        claimRepoMock.SetupSequence(c => c.LeaseAsync(It.IsAny<ClaimLeaseRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(leasedClaims)
            .ReturnsAsync(new List<ClaimKey>());

        claimRepoMock.Setup(c => c.GetBatchCountsAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((2, 0, 0));

        domainMappingsRepoMock.Setup(r => r.GetAllApprovedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ApprovedDomainMappingRow>());

        claimPayloadRepoMock.Setup(p => p.GetAsync(It.IsAny<ClaimKey>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClaimPayloadRow(new ClaimKey("provider", 100), System.Text.Encoding.UTF8.GetBytes("{\"claimHeader\": {\"proidclaim\": 100}}"), "", 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

        // Mock API response - PARTIAL SUCCESS (Only 100 succeeded, 101 failed because it's missing)
        _claimsClientMock.Setup(c => c.SendClaimAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendClaimResult(true, null, new List<long> { 100 }, 200));

        var progressMock = new Mock<IProgress<WorkerProgressReport>>();

        // Act
        await _service.ProcessBatchSenderAsync(batch, progressMock.Object, CancellationToken.None);

        // Assert
        // Check that MarkEnqueuedAsync was called with the success key
        claimRepoMock.Verify(c => c.MarkEnqueuedAsync(
            It.Is<IReadOnlyList<ClaimKey>>(keys => keys.Count == 1 && keys[0].ProIdClaim == 100),
            It.IsAny<string>(),
            It.IsAny<DateTimeOffset>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Check that MarkFailedAsync was called with the failure key (deterministically computed)
        claimRepoMock.Verify(c => c.MarkFailedAsync(
            It.Is<IReadOnlyList<ClaimKey>>(keys => keys.Count == 1 && keys[0].ProIdClaim == 101),
            It.IsAny<string>(),
            It.IsAny<DateTimeOffset>(),
            It.IsAny<TimeSpan?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessBatchSenderAsync_ZeroSuccess_ShouldMarkAllFailures()
    {
        // Setup
        var batch = new BatchRow(1, "provider", "company", "payer", "202401", null, null, "bcr-123", BatchStatus.Ready, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, 0, 0, "", 0);

        var uowMock = new Mock<ISqliteUnitOfWork>();
        var claimRepoMock = new Mock<IClaimRepository>();
        var claimPayloadRepoMock = new Mock<IClaimPayloadRepository>();
        var dispatchRepoMock = new Mock<IDispatchRepository>();
        var dispatchItemRepoMock = new Mock<IDispatchItemRepository>();
        var batchRepoMock = new Mock<IBatchRepository>();
        var domainMappingsRepoMock = new Mock<IDomainMappingRepository>();

        uowMock.Setup(u => u.Claims).Returns(claimRepoMock.Object);
        uowMock.Setup(u => u.ClaimPayloads).Returns(claimPayloadRepoMock.Object);
        uowMock.Setup(u => u.Dispatches).Returns(dispatchRepoMock.Object);
        uowMock.Setup(u => u.DispatchItems).Returns(dispatchItemRepoMock.Object);
        uowMock.Setup(u => u.Batches).Returns(batchRepoMock.Object);
        uowMock.Setup(u => u.DomainMappings).Returns(domainMappingsRepoMock.Object);

        _uowFactoryMock.Setup(f => f.CreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(uowMock.Object);

        // First pass: return 2 claims to lease
        var claim1 = new ClaimKey("provider", 100);
        var claim2 = new ClaimKey("provider", 101);
        var leasedClaims = new List<ClaimKey> { claim1, claim2 };

        claimRepoMock.SetupSequence(c => c.LeaseAsync(It.IsAny<ClaimLeaseRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(leasedClaims)
            .ReturnsAsync(new List<ClaimKey>());

        claimRepoMock.Setup(c => c.GetBatchCountsAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((2, 0, 0));

        domainMappingsRepoMock.Setup(r => r.GetAllApprovedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ApprovedDomainMappingRow>());

        claimPayloadRepoMock.Setup(p => p.GetAsync(It.IsAny<ClaimKey>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClaimPayloadRow(new ClaimKey("provider", 100), System.Text.Encoding.UTF8.GetBytes("{\"claimHeader\": {\"proidclaim\": 100}}"), "", 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

        // Mock API response - ZERO SUCCESS (empty success list)
        _claimsClientMock.Setup(c => c.SendClaimAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendClaimResult(true, null, new List<long>(), 200));

        var progressMock = new Mock<IProgress<WorkerProgressReport>>();

        // Act
        await _service.ProcessBatchSenderAsync(batch, progressMock.Object, CancellationToken.None);

        // Assert
        // Check that MarkEnqueuedAsync was not called
        claimRepoMock.Verify(c => c.MarkEnqueuedAsync(
            It.IsAny<IReadOnlyList<ClaimKey>>(),
            It.IsAny<string>(),
            It.IsAny<DateTimeOffset>(),
            It.IsAny<CancellationToken>()), Times.Never);

        // Check that MarkFailedAsync was called with both keys
        claimRepoMock.Verify(c => c.MarkFailedAsync(
            It.Is<IReadOnlyList<ClaimKey>>(keys => keys.Count == 2),
            It.IsAny<string>(),
            It.IsAny<DateTimeOffset>(),
            It.IsAny<TimeSpan?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessBatchSenderAsync_InvalidResponse_ShouldMarkAllFailures()
    {
        // Setup
        var batch = new BatchRow(1, "provider", "company", "payer", "202401", null, null, "bcr-123", BatchStatus.Ready, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, 0, 0, "", 0);

        var uowMock = new Mock<ISqliteUnitOfWork>();
        var claimRepoMock = new Mock<IClaimRepository>();
        var claimPayloadRepoMock = new Mock<IClaimPayloadRepository>();
        var dispatchRepoMock = new Mock<IDispatchRepository>();
        var dispatchItemRepoMock = new Mock<IDispatchItemRepository>();
        var batchRepoMock = new Mock<IBatchRepository>();
        var domainMappingsRepoMock = new Mock<IDomainMappingRepository>();

        uowMock.Setup(u => u.Claims).Returns(claimRepoMock.Object);
        uowMock.Setup(u => u.ClaimPayloads).Returns(claimPayloadRepoMock.Object);
        uowMock.Setup(u => u.Dispatches).Returns(dispatchRepoMock.Object);
        uowMock.Setup(u => u.DispatchItems).Returns(dispatchItemRepoMock.Object);
        uowMock.Setup(u => u.Batches).Returns(batchRepoMock.Object);
        uowMock.Setup(u => u.DomainMappings).Returns(domainMappingsRepoMock.Object);

        _uowFactoryMock.Setup(f => f.CreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(uowMock.Object);

        var claim1 = new ClaimKey("provider", 100);
        var claim2 = new ClaimKey("provider", 101);
        var leasedClaims = new List<ClaimKey> { claim1, claim2 };

        claimRepoMock.SetupSequence(c => c.LeaseAsync(It.IsAny<ClaimLeaseRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(leasedClaims)
            .ReturnsAsync(new List<ClaimKey>());

        claimRepoMock.Setup(c => c.GetBatchCountsAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((2, 0, 0));

        domainMappingsRepoMock.Setup(r => r.GetAllApprovedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ApprovedDomainMappingRow>());

        claimPayloadRepoMock.Setup(p => p.GetAsync(It.IsAny<ClaimKey>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClaimPayloadRow(new ClaimKey("provider", 100), System.Text.Encoding.UTF8.GetBytes("{\"claimHeader\": {\"proidclaim\": 100}}"), "", 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

        // Mock API response - FAILED (e.g. 500 Server Error)
        _claimsClientMock.Setup(c => c.SendClaimAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendClaimResult(false, "Server error", new List<long>(), 500));

        var progressMock = new Mock<IProgress<WorkerProgressReport>>();

        // Act
        await _service.ProcessBatchSenderAsync(batch, progressMock.Object, CancellationToken.None);

        // Assert
        // Check that MarkEnqueuedAsync was not called
        claimRepoMock.Verify(c => c.MarkEnqueuedAsync(
            It.IsAny<IReadOnlyList<ClaimKey>>(),
            It.IsAny<string>(),
            It.IsAny<DateTimeOffset>(),
            It.IsAny<CancellationToken>()), Times.Never);

        // Check that MarkFailedAsync was called for all claims with the error message
        claimRepoMock.Verify(c => c.MarkFailedAsync(
            It.Is<IReadOnlyList<ClaimKey>>(keys => keys.Count == 2),
            "Server error",
            It.IsAny<DateTimeOffset>(),
            It.IsAny<TimeSpan?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
