using OrchardCore.Modules.Manifest;

[assembly: Module(
    Name = "Seo",
    Author = ManifestConstants.OrchardCoreTeam,
    Website = ManifestConstants.OrchardCoreWebsite,
    Version = ManifestConstants.OrchardCoreVersion,
    Description = "Provides SEO Meta features",
    Category = "Content Management",
    Dependencies = new string[] { "OrchardCore.Contents "}
)]
