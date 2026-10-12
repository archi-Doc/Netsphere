// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

extern alias RunnerAssembly;

using RunnerAssembly::Netsphere.Runner;
using Xunit;

namespace xUnitTest.NetsphereTest;

public class RunnerImageReviewTest
{
    [Theory]
    [InlineData("app", "app", true)]
    [InlineData("app:latest", "app", true)]
    [InlineData("app@sha256:abc", "app", true)]
    [InlineData("app-worker:latest", "app", false)]
    [InlineData("app:v10", "app:v1", false)]
    [InlineData("app:v1", "app:v1", true)]
    [InlineData("APP:v1", "app", false)]
    [InlineData("app", "", false)]
    [InlineData("", "", false)]
    [InlineData("registry:5000/app:v1", "registry:5000/app", true)]
    [InlineData("registry:5000/app-other:v1", "registry:5000/app", false)]
    public void RepositoryMatchingDoesNotSelectUnrelatedContainers(string image, string requestedImage, bool expected)
        => Assert.Equal(expected, DockerRunner.MatchesImage(image, requestedImage));

    [Theory]
    [InlineData("app", "app", "")]
    [InlineData("app:v1", "app", "v1")]
    [InlineData("registry:5000/app", "registry:5000/app", "")]
    [InlineData("registry:5000/app:v1", "registry:5000/app", "v1")]
    [InlineData("registry:5000/app@sha256:abc", "registry:5000/app@sha256:abc", "")]
    public void ImageTagsAreSeparatedFromRegistryPortsAndDigests(string reference, string image, string tag)
        => Assert.Equal((image, tag), DockerRunner.SplitImageReference(reference));
}
