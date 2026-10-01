using System;
using System.IO;
using Xunit;

namespace MobiMate.Tests;

/// <summary>가공 종류 분류표 (FR-DT-20)</summary>
public class WorkCategoryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mm-workcat-" + Guid.NewGuid().ToString("N"));
    private readonly WorkCategoryCatalog _c = WorkCategoryCatalog.LoadEmbedded();

    public WorkCategoryTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Theory]
    [InlineData("철괴(광석)")]
    [InlineData("철괴(철 광석)")]
    [InlineData("강철괴")]
    [InlineData("합금강괴")]
    [InlineData("타르")]
    [InlineData("특수강괴")]
    [InlineData("은합금괴")]
    [InlineData("운철괴")]
    public void MetalNames_FromSeungwoon(string name) => Assert.Equal(WorkKinds.Metal, _c.Classify(name, null));

    [Theory]
    [InlineData("목재")]
    [InlineData("목재+")]
    [InlineData("상급 목재")]
    [InlineData("상급 목재+")]
    [InlineData("부드러운 목재")]
    [InlineData("단단한 목재")]
    [InlineData("최상급 목재")]
    [InlineData("최상급 목재+")]
    [InlineData("구름결 막대")]
    [InlineData("특급 목재")]
    public void WoodNames_FromSeungwoon(string name) => Assert.Equal(WorkKinds.Wood, _c.Classify(name, null));

    [Theory]
    [InlineData("가죽")]
    [InlineData("가죽+")]
    [InlineData("상급 가죽")]
    [InlineData("상급 가죽+")]
    [InlineData("최상급 가죽")]
    [InlineData("최상급 가죽+")]
    [InlineData("특급 가죽")]
    public void LeatherNames_FromSeungwoon(string name) => Assert.Equal(WorkKinds.Leather, _c.Classify(name, null));

    [Fact]
    public void NameMatchIgnoresSpacesAndCase()
    {
        Assert.Equal(WorkKinds.Metal, _c.Classify("철괴 (철광석)", null));   // 표에는 "철괴(철 광석)"
        Assert.Equal(WorkKinds.Wood, _c.Classify("최상급목재", null));
    }

    [Theory]
    [InlineData("무언가", "제련로", WorkKinds.Metal)]
    [InlineData("무언가", "가죽 작업대", WorkKinds.Leather)]
    [InlineData("무언가", "베틀", WorkKinds.Cloth)]
    [InlineData("무언가", "약품 작업대", WorkKinds.Medicine)]
    public void FacilityDecidesWhenTheNameIsUnknown(string name, string facility, string kind) => Assert.Equal(kind, _c.Classify(name, facility));

    [Theory]
    [InlineData("고급 옷감", WorkKinds.Cloth)]
    [InlineData("최고급 실크", WorkKinds.Cloth)]
    [InlineData("체력 포션", WorkKinds.Medicine)]
    [InlineData("마나 물약", WorkKinds.Medicine)]
    [InlineData("밀가루", WorkKinds.Food)]
    [InlineData("버터", WorkKinds.Food)]
    [InlineData("신비한 광석", WorkKinds.Metal)]
    public void KeywordsAreTheLastResort(string name, string kind) => Assert.Equal(kind, _c.Classify(name, "알 수 없는 작업대"));

    [Fact]
    public void UnknownGoesToOther_AndLabelsExist()
    {
        Assert.Equal(WorkKinds.Other, _c.Classify("정체 모를 물건", "정체 모를 작업대"));
        Assert.Equal(WorkKinds.Other, _c.Classify(null, null));
        foreach (var k in WorkKinds.Order) Assert.False(string.IsNullOrWhiteSpace(_c.LabelOf(k)));
        Assert.Equal("금속 가공", _c.LabelOf(WorkKinds.Metal));
    }

    [Fact]
    public void OverrideFile_AddsNamesAndFacilities_WithoutCode()
    {
        File.WriteAllText(Path.Combine(_dir, WorkCategoryCatalog.OverrideFileName), """
            { "items": { "food": ["특제 반죽면"], "metal": ["타르"] },
              "facilities": { "오븐": "food" } }
            """);
        var c = WorkCategoryCatalog.Load(_dir);
        Assert.Equal(WorkKinds.Food, c.Classify("특제 반죽면", null));
        Assert.Equal(WorkKinds.Food, c.Classify("뭔가", "오븐"));
        Assert.Equal(WorkKinds.Metal, c.Classify("타르", null));   // 기존 분류도 그대로
        Assert.Empty(c.Warnings);
    }

    [Fact]
    public void BrokenOverrideFile_IsIgnoredWithAWarning()
    {
        File.WriteAllText(Path.Combine(_dir, WorkCategoryCatalog.OverrideFileName), "{ 깨진 파일");
        var c = WorkCategoryCatalog.Load(_dir);
        Assert.Single(c.Warnings);
        Assert.Equal(WorkKinds.Metal, c.Classify("강철괴", null));
    }
}
