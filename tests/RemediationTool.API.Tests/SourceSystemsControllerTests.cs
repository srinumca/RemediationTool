using Microsoft.AspNetCore.Mvc;
using Moq;
using RemediationTool.API.Controllers;
using RemediationTool.Application.Models;
using RemediationTool.Application.Repositories;
using RemediationTool.Domain.Entities;
using Xunit;

namespace RemediationTool.API.Tests;

public sealed class SourceSystemsControllerTests
{
    [Fact]
    public async Task GetEnabled_ReturnsEnabledSourceSystemsForDropdown()
    {
        var repository = new Mock<ISourceSystemRepository>(MockBehavior.Strict);
        repository
            .Setup(item => item.GetEnabledAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new SourceSystemDefinition
                {
                    SourceSystem = "NetApp",
                    DisplayName = "NetApp File Server",
                    DataSystem = "NetApp",
                    OriginatingDataSystem = "smb",
                    IsEnabled = true
                }
            });
        var controller = new SourceSystemsController(repository.Object);

        var result = await controller.GetEnabled(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var options = Assert.IsAssignableFrom<IReadOnlyList<SourceSystemOption>>(ok.Value);
        var option = Assert.Single(options);
        Assert.Equal("NetApp", option.SourceSystem);
        Assert.Equal("NetApp File Server", option.DisplayName);
        Assert.Equal("NetApp", option.DataSystem);
        Assert.Equal("smb", option.OriginatingDataSystem);
        repository.Verify(
            item => item.GetEnabledAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetEnabled_ExcludesInvalidOrDisabledDefinitions()
    {
        var repository = new Mock<ISourceSystemRepository>(MockBehavior.Strict);
        repository
            .Setup(item => item.GetEnabledAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new SourceSystemDefinition
                {
                    SourceSystem = "NetApp",
                    IsEnabled = true
                },
                new SourceSystemDefinition
                {
                    SourceSystem = "Disabled",
                    IsEnabled = false
                },
                new SourceSystemDefinition
                {
                    SourceSystem = " ",
                    IsEnabled = true
                }
            });
        var controller = new SourceSystemsController(repository.Object);

        var result = await controller.GetEnabled(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var options = Assert.IsAssignableFrom<IReadOnlyList<SourceSystemOption>>(ok.Value);
        var option = Assert.Single(options);
        Assert.Equal("NetApp", option.SourceSystem);
        Assert.Equal("NetApp", option.DisplayName);
    }
}
