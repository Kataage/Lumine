namespace Lumine.Library;

public sealed record CreativeAssetRef(
    long Id,
    string FileName,
    string RelativePath);

public sealed record WorkInfo(
    long Id,
    long LibraryId,
    string Title,
    string Description,
    long? CoverAssetId,
    IReadOnlyList<CreativeAssetRef> Assets,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record WorkCreate(
    string Title,
    string Description,
    IReadOnlyList<long> AssetIds);

public sealed record GenerationGroupInfo(
    long Id,
    long LibraryId,
    long? WorkId,
    string Name,
    string Prompt,
    string NegativePrompt,
    string ModelName,
    string Sampler,
    string Scheduler,
    int Steps,
    double CfgScale,
    string WorkflowJson,
    string Notes,
    IReadOnlyList<CreativeAssetRef> Assets,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record GenerationGroupCreate(
    string Name,
    IReadOnlyList<long> AssetIds,
    long? WorkId = null,
    string Prompt = "",
    string NegativePrompt = "",
    string ModelName = "",
    string Sampler = "",
    string Scheduler = "",
    int Steps = 0,
    double CfgScale = 0,
    string WorkflowJson = "",
    string Notes = "");

public sealed record AssetRelationInfo(
    long Id,
    long LibraryId,
    CreativeAssetRef Parent,
    CreativeAssetRef Child,
    string RelationType,
    string Note,
    DateTimeOffset CreatedAtUtc);

public sealed record AssetRelationCreate(
    long ParentAssetId,
    long ChildAssetId,
    string RelationType,
    string Note = "");

public sealed record PublicationDestinationInfo(
    long Id,
    long LibraryId,
    string Name,
    string Kind,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record PublicationDestinationCreate(
    string Name,
    string Kind);

public sealed record PublicationAccountInfo(
    long Id,
    long LibraryId,
    long DestinationId,
    string DisplayName,
    string AccountIdentifier,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record PublicationAccountCreate(
    long DestinationId,
    string DisplayName,
    string AccountIdentifier = "");

public sealed record PublicationAssetSnapshot(
    long? AssetId,
    string FileName,
    string RelativePath,
    int SortOrder);

public sealed record PublicationInfo(
    long Id,
    long LibraryId,
    long? WorkId,
    string Title,
    string Body,
    string TagsSnapshot,
    string Destination,
    string Account,
    DateTimeOffset PublishedAtUtc,
    string ExternalId,
    string ExternalUrl,
    string PlatformMetadataJson,
    IReadOnlyList<PublicationAssetSnapshot> Assets,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    long AssetCount = -1);

public sealed record PublicationCreate(
    IReadOnlyList<long> AssetIds,
    string Destination,
    DateTimeOffset PublishedAtUtc,
    long? WorkId = null,
    string Title = "",
    string Body = "",
    string TagsSnapshot = "",
    string Account = "",
    string ExternalId = "",
    string ExternalUrl = "",
    string PlatformMetadataJson = "{}");

public sealed record PublicationCursor(
    DateTimeOffset PublishedAtUtc,
    long Id);

public sealed record PublicationPage(
    IReadOnlyList<PublicationInfo> Items,
    PublicationCursor? NextCursor,
    long TotalCount);

public sealed record AssetCreativeContext(
    IReadOnlyList<WorkInfo> Works,
    IReadOnlyList<GenerationGroupInfo> GenerationGroups,
    IReadOnlyList<AssetRelationInfo> Relations,
    IReadOnlyList<PublicationInfo> Publications,
    long PublicationCount = 0);
