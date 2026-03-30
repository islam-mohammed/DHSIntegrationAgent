using System;
using System.Threading;
using System.Threading.Tasks;
using DHSIntegrationAgent.Application.Abstractions;
using DHSIntegrationAgent.Application.Persistence;
using DHSIntegrationAgent.Application.Persistence.Repositories;
using DHSIntegrationAgent.Application.Services;
using FluentAssertions;
using Moq;
using Xunit;

namespace DHSIntegrationAgent.Tests.Unit.Services;

public class DeleteBatchServiceTests
{
    private readonly Mock<ISqliteUnitOfWorkFactory> _uowFactoryMock;
    private readonly Mock<ISqliteUnitOfWork> _uowMock;
    private readonly Mock<IBatchRepository> _batchRepoMock;
    private readonly Mock<IBatchClient> _batchClientMock;
    private readonly Mock<IBatchRegistry> _batchRegistryMock;

    private readonly DeleteBatchService _sut;

    public DeleteBatchServiceTests()
    {
        _uowFactoryMock = new Mock<ISqliteUnitOfWorkFactory>();
        _uowMock = new Mock<ISqliteUnitOfWork>();
        _batchRepoMock = new Mock<IBatchRepository>();
        _batchClientMock = new Mock<IBatchClient>();
        _batchRegistryMock = new Mock<IBatchRegistry>();

        _uowMock.Setup(u => u.Batches).Returns(_batchRepoMock.Object);
        _uowFactoryMock.Setup(f => f.CreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_uowMock.Object);

        _sut = new DeleteBatchService(
            _uowFactoryMock.Object,
            _batchClientMock.Object,
            _batchRegistryMock.Object);
    }

    [Fact]
    public async Task DeleteBatchAsync_WhenBatchIsRegistered_ReturnsFalse()
    {
        // Arrange
        long localBatchId = 123;
        _batchRegistryMock.Setup(r => r.TryRegister(localBatchId)).Returns(false);

        // Act
        var result = await _sut.DeleteBatchAsync(localBatchId, null, CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Contain("currently being processed");

        // Ensure no API or DB calls were made
        _batchClientMock.Verify(c => c.DeleteBatchAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _batchRepoMock.Verify(r => r.UpdateStatusAsync(It.IsAny<long>(), It.IsAny<DHSIntegrationAgent.Domain.WorkStates.BatchStatus>(), It.IsAny<bool?>(), It.IsAny<string?>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task DeleteBatchAsync_WithBcrId_DeletesFromServerAndLocal()
    {
        // Arrange
        long localBatchId = 123;
        string bcrId = "456";
        _batchRegistryMock.Setup(r => r.TryRegister(localBatchId)).Returns(true);
        _batchClientMock.Setup(c => c.DeleteBatchAsync(456, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteBatchResult(true, 200, "Success", null, true));

        // Act
        var result = await _sut.DeleteBatchAsync(localBatchId, bcrId, CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();

        _batchClientMock.Verify(c => c.DeleteBatchAsync(456, It.IsAny<CancellationToken>()), Times.Once);
        _batchRepoMock.Verify(r => r.UpdateStatusAsync(localBatchId, DHSIntegrationAgent.Domain.WorkStates.BatchStatus.Deleted, null, null, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>(), null), Times.Once);
        _uowMock.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);

        // Ensure registry tracking logic ran
        _batchRegistryMock.Verify(r => r.TryRegister(localBatchId), Times.Once);
        _batchRegistryMock.Verify(r => r.Unregister(localBatchId), Times.Once);
    }

    [Fact]
    public async Task DeleteBatchAsync_WhenApiCallFails_ReturnsFalseAndDoesNotDeleteLocally()
    {
        // Arrange
        long localBatchId = 123;
        string bcrId = "456";
        _batchRegistryMock.Setup(r => r.TryRegister(localBatchId)).Returns(true);
        _batchClientMock.Setup(c => c.DeleteBatchAsync(456, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteBatchResult(false, 500, "Server Error", null, false));

        // Act
        var result = await _sut.DeleteBatchAsync(localBatchId, bcrId, CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Server Error");

        _batchClientMock.Verify(c => c.DeleteBatchAsync(456, It.IsAny<CancellationToken>()), Times.Once);
        _batchRepoMock.Verify(r => r.UpdateStatusAsync(It.IsAny<long>(), It.IsAny<DHSIntegrationAgent.Domain.WorkStates.BatchStatus>(), It.IsAny<bool?>(), It.IsAny<string?>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()), Times.Never);
        _uowMock.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteBatchAsync_WithoutBcrId_DeletesOnlyLocally()
    {
        // Arrange
        long localBatchId = 123;
        string? bcrId = null;
        _batchRegistryMock.Setup(r => r.TryRegister(localBatchId)).Returns(true);

        // Act
        var result = await _sut.DeleteBatchAsync(localBatchId, bcrId, CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();

        _batchClientMock.Verify(c => c.DeleteBatchAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _batchRepoMock.Verify(r => r.UpdateStatusAsync(localBatchId, DHSIntegrationAgent.Domain.WorkStates.BatchStatus.Deleted, null, null, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>(), null), Times.Once);
        _uowMock.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
