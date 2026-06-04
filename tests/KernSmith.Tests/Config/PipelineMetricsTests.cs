using Shouldly;

namespace KernSmith.Tests.Config;

public sealed class PipelineMetricsTests
{
    [Fact]
    public void NewMetrics_AllStagesZero()
    {
        // Act
        var metrics = new PipelineMetrics();

        // Assert
        metrics.FontParsing.ShouldBe(TimeSpan.Zero);
        metrics.Rasterization.ShouldBe(TimeSpan.Zero);
        metrics.AtlasPacking.ShouldBe(TimeSpan.Zero);
        metrics.Total.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void BeginEnd_AccumulatesStageTime()
    {
        // Arrange
        var metrics = new PipelineMetrics();

        // Act
        metrics.Begin("FontParsing");
        Thread.Sleep(2);
        metrics.End();

        // Assert
        metrics.FontParsing.ShouldBeGreaterThan(TimeSpan.Zero);
        metrics.Total.ShouldBe(metrics.FontParsing);
    }

    [Fact]
    public void BeginEnd_MultipleStages_AddToTotal()
    {
        // Arrange
        var metrics = new PipelineMetrics();

        // Act
        metrics.Begin("Rasterization");
        Thread.Sleep(1);
        metrics.End();
        metrics.Begin("AtlasPacking");
        Thread.Sleep(1);
        metrics.End();

        // Assert
        metrics.Rasterization.ShouldBeGreaterThan(TimeSpan.Zero);
        metrics.AtlasPacking.ShouldBeGreaterThan(TimeSpan.Zero);
        metrics.Total.ShouldBe(metrics.Rasterization + metrics.AtlasPacking);
    }

    [Fact]
    public void ToString_OmitsZeroStages()
    {
        // Arrange
        var metrics = new PipelineMetrics();
        metrics.Begin("Rasterization");
        Thread.Sleep(1);
        metrics.End();

        // Act
        var report = metrics.ToString();

        // Assert
        report.ShouldContain("Rasterization");
        report.ShouldContain("Total");
        report.ShouldNotContain("Font parsing");
    }

    [Fact]
    public void End_UnknownStage_DoesNotThrowAndStillTracksTotal()
    {
        // Arrange
        var metrics = new PipelineMetrics();

        // Act
        metrics.Begin("NotARealStage");
        Thread.Sleep(1);
        metrics.End();

        // Assert — total still accumulates even when the stage name is unmapped
        metrics.Total.ShouldBeGreaterThan(TimeSpan.Zero);
    }
}
