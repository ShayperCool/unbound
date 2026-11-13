using System;
using UnityEditor.PackageManager;
using Xunit;

namespace UnityEngine.Tests;

public class EditorDeclarationStorageTests
{
    [Fact]
    public void GetOnlyPackageValuesKeepFactoryFallbacksAndArrayIdentity()
    {
        string[] keywords = { "first" };
        var package = PackageInfo.Create("package", "2.3", PackageSource.Local, "/resolved", "", "description", "category", "library", keywords, true);
        Assert.Equal("package", package.displayName);
        Assert.Equal("package@2.3", package.packageId);
        Assert.Equal("/resolved", package.resolvedPath);
        Assert.Equal(PackageSource.Local, package.source);
        Assert.Equal("description", package.description);
        Assert.Equal("category", package.category);
        Assert.Equal("library", package.type);
        Assert.True(package.isDirectDependency);
        Assert.Same(keywords, package.keywords);
        keywords[0] = "after";
        Assert.Equal("after", package.keywords[0]);
        var fallback = PackageInfo.Create("other", null, PackageSource.Unknown, null, "explicit", null, null, null, null, false);
        Assert.Equal("explicit", fallback.displayName);
        Assert.Empty(fallback.keywords);
        Assert.Null(fallback.version);
        Assert.Null(fallback.description);
        Assert.False(fallback.isDirectDependency);
    }

    [Fact]
    public void DependencyValuesSurviveCopiesWithExplicitGetters()
    {
        var first = new DependencyInfo("first", "1");
        var copy = first;
        first = new DependencyInfo("other", "2");
        Assert.Equal("first", copy.name);
        Assert.Equal("1", copy.version);
        Assert.Equal("other", first.name);
        Assert.Equal("2", first.version);
        Assert.Null(default(DependencyInfo).name);
    }
}
