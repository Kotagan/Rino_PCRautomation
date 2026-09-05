using MFAWPF.Configuration;
using MFAWPF.Services.RinoPackages;
using MFAWPF.Models.Characters;
using MFAWPF.Services.Characters;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using System.IO.Compression;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MFAWPF.PackageTests;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private static async Task<int> Main(string[] arguments)
    {
        if (arguments.FirstOrDefault() == "--fake-rino-sidecar")
        {
            return await RunFakeRinoSidecarAsync(arguments.Skip(1).FirstOrDefault() ?? "normal");
        }

        var testRoot = Path.Combine(
            Path.GetTempPath(),
            $"rino-package-contract-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(testRoot);

        var failures = new List<string>();
        try
        {
            await RunAsync("desktop character configuration is bounded and portable", failures, () =>
            {
                var characterRoot = Path.Combine(testRoot, "character");
                var assetRoot = Path.Combine(characterRoot, "Pets");
                var configurationPath = Path.Combine(characterRoot, "config", "character.json");
                var paths = CharacterStoragePaths.CreateForTesting(assetRoot, configurationPath);
                var firstProfileDirectory = paths.CreateProfileDirectory("Character");
                var secondProfileDirectory = paths.CreateProfileDirectory("Character");
                Assert(Directory.Exists(firstProfileDirectory), "Expected a character asset directory.");
                Assert(Directory.Exists(secondProfileDirectory), "Expected a second character asset directory.");
                Assert(
                    Path.GetFileName(firstProfileDirectory) == "Character",
                    "Expected the profile name as the asset directory name.");
                Assert(
                    Path.GetFileName(secondProfileDirectory) == "Character (2)",
                    "Expected a unique suffix for duplicate profile directory names.");
                var profile = CharacterProfile.CreateDefault();
                profile.Size = 4096;
                profile.MovementSpeed = 999;
                profile.FootAnchorY = -1;
                profile.IdleAnimations[0].PlaybackRate = 99;
                profile.IdleAnimations.Add(new CharacterAnimationDefinition());
                profile.Normalize();

                Assert(profile.Size == 512, "Expected bounded character size.");
                Assert(profile.MovementSpeed == 300, "Expected bounded movement speed.");
                Assert(profile.FootAnchorY == 0, "Expected bounded foot anchor.");
                Assert(profile.IdleAnimations.Count == 6, "Expected exactly six idle slots.");
                Assert(profile.IdleAnimations[0].PlaybackRate == 3, "Expected bounded playback rate.");

                var insidePath = Path.Combine(assetRoot, "Cats", "idle.gif");
                Directory.CreateDirectory(Path.GetDirectoryName(insidePath)!);
                File.WriteAllBytes(
                    insidePath,
                    Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw=="));
                Assert(
                    paths.TryCreateRelativePath(insidePath, out var relativePath),
                    "Expected an asset-root relative path.");
                Assert(
                    relativePath == Path.Combine("Cats", "idle.gif"),
                    "Expected portable relative asset path.");
                Assert(
                    !paths.TryCreateRelativePath(
                        Path.Combine(characterRoot, "outside.gif"),
                        out _),
                    "Expected outside asset path rejection.");
                Assert(
                    paths.ResolveAssetPath(Path.Combine("..", "outside.gif")) is null,
                    "Expected traversal path rejection.");

                var settings = CharacterSettings.CreateDefault();
                settings.IsEnabled = true;
                settings.GetActiveProfile().IdleAnimations[0].SourcePath = relativePath;
                var store = new CharacterSettingsStore(paths);
                store.Save(settings);
                var loaded = store.Load();
                Assert(loaded.IsEnabled, "Expected persisted character enable state.");
                Assert(
                    loaded.GetActiveProfile().IdleAnimations[0].SourcePath == relativePath,
                    "Expected persisted portable animation path.");

                var clip = new CharacterAnimationLoader(paths).Load(
                    loaded.GetActiveProfile().IdleAnimations[0]);
                Assert(!clip.IsEmpty, "Expected GIF animation decoding.");
                Assert(clip.Frames.Count == 1, "Expected one GIF frame.");

                var frameDirectory = Path.Combine(assetRoot, "Cats", "idle-frames");
                Directory.CreateDirectory(frameDirectory);
                File.WriteAllBytes(
                    Path.Combine(frameDirectory, "001.png"),
                    Convert.FromBase64String(
                        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
                var frameClip = new CharacterAnimationLoader(paths).Load(
                    new CharacterAnimationDefinition
                    {
                        SourcePath = Path.GetRelativePath(assetRoot, frameDirectory),
                    });
                Assert(!frameClip.IsEmpty, "Expected PNG frame-directory decoding.");
                Assert(frameClip.Frames.Count == 1, "Expected one PNG sequence frame.");

                // Verify image loading does not lock the file on disk
                using (var exclusiveStream = new FileStream(insidePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    Assert(exclusiveStream.Length > 0, "Expected non-locked image file accessible exclusively.");
                }

                // Verify default profile directory creation
                var ensuredDirectory = paths.EnsureProfileDirectory("DefaultCharacter");
                Assert(Directory.Exists(ensuredDirectory), "Expected ensured default character profile directory.");
                Assert(Path.GetFileName(ensuredDirectory) == "DefaultCharacter", "Expected profile name as directory name.");

                // Verify renaming profile directory
                Assert(
                    paths.TryRenameProfileDirectory("DefaultCharacter", "RenamedCharacter", out var renamedDir, out var renameError),
                    $"Expected rename to succeed, got: {renameError}");
                Assert(renamedDir == "RenamedCharacter", "Expected renamed directory name.");
                Assert(!Directory.Exists(ensuredDirectory), "Expected old directory to be moved.");
                Assert(Directory.Exists(Path.Combine(assetRoot, "RenamedCharacter")), "Expected new directory to exist.");

                // Verify renaming collision handling
                paths.EnsureProfileDirectory("ExistingOther");
                Assert(
                    !paths.TryRenameProfileDirectory("RenamedCharacter", "ExistingOther", out _, out var collisionError),
                    "Expected duplicate/existing directory rename to fail.");
                Assert(collisionError == "CharacterProfileDirectoryExists", "Expected collision error code.");

                // Verify deleting profile directory
                Assert(paths.TryDeleteProfileDirectory("RenamedCharacter"), "Expected profile directory deletion to succeed.");
                Assert(!Directory.Exists(Path.Combine(assetRoot, "RenamedCharacter")), "Expected deleted profile directory to not exist.");

                // Verify cleaning up empty orphaned directories
                var orphanedEmpty = paths.EnsureProfileDirectory("OrphanedEmpty");
                var orphanedWithFile = paths.EnsureProfileDirectory("OrphanedWithFile");
                File.WriteAllText(Path.Combine(orphanedWithFile, "keep.txt"), "data");
                var activeDir = paths.EnsureProfileDirectory("ActiveProfile");

                paths.CleanupEmptyOrphanedProfileDirectories(["ActiveProfile"]);
                Assert(!Directory.Exists(orphanedEmpty), "Expected empty orphaned directory to be cleaned up.");
                Assert(Directory.Exists(orphanedWithFile), "Expected non-empty orphaned directory to be preserved.");
                Assert(Directory.Exists(activeDir), "Expected active profile directory to be preserved.");

                return Task.CompletedTask;
            });

            await RunAsync("desktop character task state is typed", failures, () =>
            {
                var service = new CharacterTaskStateService();
                CharacterTaskStateChangedEventArgs? received = null;
                service.StateChanged += (_, args) => received = args;
                service.Notify(CharacterTaskState.TaskStarted, "entry.main", "Main");

                Assert(service.CurrentState == CharacterTaskState.TaskStarted, "Expected current task state.");
                Assert(received?.TaskKey == "entry.main", "Expected stable top-level task key.");
                Assert(received?.TaskName == "Main", "Expected top-level task display name.");
                return Task.CompletedTask;
            });

            await RunAsync("valid signed project package", failures, async () =>
            {
                var package = await CreatePackageAsync(testRoot, "valid");
                var result = await CreateValidator().ValidateAsync(
                    package.ArchivePath,
                    package.ArchiveSha256,
                    package.PublisherKey,
                    CancellationToken.None);

                Assert(result.Manifest.PackageType == "project", "Expected project package type.");
                Assert(result.Manifest.Payload.DefaultProjectEntrypointId == "main", "Expected main entrypoint.");
            });

            await RunAsync("task sequence preset applies order and sequence badges to home queue", failures, () =>
            {
                var queueVm = new MFAWPF.ViewModels.UI.TaskQueueViewModel();
                var task1 = new MFAWPF.ViewModels.Tool.DragItemViewModel(new MFAWPF.Helper.ValueType.TaskInterfaceItem { Name = "每日签到" });
                var task2 = new MFAWPF.ViewModels.Tool.DragItemViewModel(new MFAWPF.Helper.ValueType.TaskInterfaceItem { Name = "启动游戏" });
                var task3 = new MFAWPF.ViewModels.Tool.DragItemViewModel(new MFAWPF.Helper.ValueType.TaskInterfaceItem { Name = "领取邮件" });
                queueVm.TaskItemViewModels = new System.Collections.ObjectModel.ObservableCollection<MFAWPF.ViewModels.Tool.DragItemViewModel> { task1, task2, task3 };

                var presetSequence = new List<MFAWPF.Models.TaskPresetItem>
                {
                    new() { TaskName = "启动游戏", Order = 1 },
                    new() { TaskName = "每日签到", Order = 2 },
                };

                queueVm.ApplyPresetSequence(presetSequence, "测试预设");

                Assert(queueVm.IsPresetMode, "Expected preset mode active.");
                Assert(queueVm.TaskItemViewModels.Count == 3, "Expected 3 tasks in queue.");

                Assert(queueVm.TaskItemViewModels[0].Name == "启动游戏", "Expected 启动游戏 at position 0.");
                Assert(queueVm.TaskItemViewModels[0].SequenceOrder == 1, "Expected order 1 for 启动游戏.");
                Assert(queueVm.TaskItemViewModels[0].IsSequenceBadgeVisible, "Expected badge visible for 启动游戏.");
                Assert(queueVm.TaskItemViewModels[0].IsChecked, "Expected 启动游戏 checked.");

                Assert(queueVm.TaskItemViewModels[1].Name == "每日签到", "Expected 每日签到 at position 1.");
                Assert(queueVm.TaskItemViewModels[1].SequenceOrder == 2, "Expected order 2 for 每日签到.");
                Assert(queueVm.TaskItemViewModels[1].IsSequenceBadgeVisible, "Expected badge visible for 每日签到.");
                Assert(queueVm.TaskItemViewModels[1].IsChecked, "Expected 每日签到 checked.");

                Assert(queueVm.TaskItemViewModels[2].Name == "领取邮件", "Expected 领取邮件 at position 2.");
                Assert(queueVm.TaskItemViewModels[2].SequenceOrder == null, "Expected null sequence order for unassigned 领取邮件.");
                Assert(!queueVm.TaskItemViewModels[2].IsSequenceBadgeVisible, "Expected badge hidden for unassigned 领取邮件.");
                Assert(!queueVm.TaskItemViewModels[2].IsChecked, "Expected 领取邮件 unchecked.");

                queueVm.ExitPresetMode();
                Assert(!queueVm.IsPresetMode, "Expected preset mode inactive.");
                Assert(queueVm.TaskItemViewModels[0].SequenceOrder == null, "Expected cleared order after exit.");
                Assert(!queueVm.TaskItemViewModels[0].IsSequenceBadgeVisible, "Expected cleared badge after exit.");

                return Task.CompletedTask;
            });

            await RunAsync("invalid trust public key rejected", failures, () =>
            {
                try
                {
                    _ = new RinoPackageSigningPublicKey("catalog.root.v1", new byte[31]);
                }
                catch (ArgumentException)
                {
                    return Task.CompletedTask;
                }

                throw new InvalidOperationException("Expected invalid Ed25519 public key rejection.");
            });

            await RunAsync("production catalog trust is pinned", failures, () =>
            {
                var source = RinoPackageDistributionTrust.CreateCatalogSource();
                var rootKey = RinoPackageDistributionTrust.CreateCatalogRootKey();

                Assert(source.Owner == "SweetSmellFox", "Expected pinned catalog owner.");
                Assert(source.Repository == "MFAWPF", "Expected pinned catalog repository.");
                Assert(rootKey.KeyId == "catalog-root-2026-08", "Expected pinned catalog root key ID.");
                Assert(rootKey.PublicKeyBytes.Length == 32, "Expected Ed25519 public key length.");
                return Task.CompletedTask;
            });

            await RunAsync("production empty catalog is valid and signed", failures, async () =>
            {
                var catalogDirectory = Path.Combine(AppContext.BaseDirectory, "distribution-catalog");
                var catalogBytes = await File.ReadAllBytesAsync(
                    Path.Combine(catalogDirectory, "catalog-v1.json"));
                var signatureBytes = await File.ReadAllBytesAsync(
                    Path.Combine(catalogDirectory, "catalog-v1.sig"));
                var catalog = new RinoPackageCatalogReader().Read(
                    catalogBytes,
                    signatureBytes,
                    RinoPackageDistributionTrust.CreateCatalogRootKey(),
                    DateTimeOffset.Parse("2026-08-10T01:00:00Z"));

                Assert(catalog.Catalog.Packages.Count == 0, "Expected initial empty package catalog.");
                Assert(!catalog.IsExpired, "Expected initial catalog validity window.");
            });

            await RunAsync("Rino sidecar framing round trip", failures, async () =>
            {
                var requestId = Guid.NewGuid().ToString("D");
                var frame = RinoSidecarProtocol.EncodeRequest(
                    "system.health",
                    requestId,
                    new { },
                    RinoSidecarProtocol.DefaultMaximumFrameBytes);
                await using var stream = new MemoryStream(frame);
                var envelope = await RinoSidecarProtocol.ReadEnvelopeAsync(
                    stream,
                    RinoSidecarProtocol.DefaultMaximumFrameBytes,
                    CancellationToken.None);

                var decoded = envelope
                    ?? throw new InvalidOperationException("Expected one decoded sidecar frame.");
                Assert(decoded.MessageKind == "request", "Expected request envelope kind.");
                Assert(decoded.RequestId == requestId, "Expected correlated request ID.");

                await using var malformed = new MemoryStream(
                    Encoding.ASCII.GetBytes("Length: 2\r\n\r\n{}"));
                await ExpectProtocolFailureAsync(
                    "sidecar.malformedHeader",
                    () => RinoSidecarProtocol.ReadEnvelopeAsync(
                        malformed,
                        RinoSidecarProtocol.DefaultMaximumFrameBytes,
                        CancellationToken.None));

                await using var truncated = new MemoryStream(
                    Encoding.ASCII.GetBytes("Content-Length: 4\r\n\r\n{}"));
                await ExpectProtocolFailureAsync(
                    "sidecar.truncatedFrame",
                    () => RinoSidecarProtocol.ReadEnvelopeAsync(
                        truncated,
                        RinoSidecarProtocol.DefaultMaximumFrameBytes,
                        CancellationToken.None));

                await using var oversized = new MemoryStream(Encoding.ASCII.GetBytes(
                    $"Content-Length: {RinoSidecarProtocol.DefaultMaximumFrameBytes + 1}\r\n\r\n"));
                await ExpectProtocolFailureAsync(
                    "sidecar.frameTooLarge",
                    () => RinoSidecarProtocol.ReadEnvelopeAsync(
                        oversized,
                        RinoSidecarProtocol.DefaultMaximumFrameBytes,
                        CancellationToken.None));
            });

            await RunAsync("Rino sidecar handshake and graph preflight", failures, async () =>
            {
                var executablePath = ResolveTestExecutable();
                await using var client = new RinoSidecarClient(
                    new RinoSidecarLaunchOptions(
                        executablePath,
                        ["--fake-rino-sidecar", "normal"]),
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(1));
                var handshake = await client.EnsureStartedAsync(CancellationToken.None);
                var document = JsonSerializer.SerializeToElement(new
                {
                    schemaVersion = 1,
                    documentId = "20000000-0000-4000-8000-000000000001",
                    metadata = new
                    {
                        name = "Preflight",
                        createdAt = "2026-08-10T00:00:00Z",
                        updatedAt = "2026-08-10T00:00:00Z",
                    },
                    entryGraphId = "20000000-0000-4000-8000-000000000002",
                    graphs = Array.Empty<object>(),
                    assets = Array.Empty<object>(),
                    requiredCapabilities = Array.Empty<string>(),
                });
                var validation = await client.ValidateGraphAsync(
                    document,
                    CancellationToken.None);

                Assert(handshake.RuntimeMode == "frozen", "Expected fake frozen runtime mode.");
                Assert(validation.Executable, "Expected executable graph preflight result.");
            });

            await RunAsync("Rino sidecar timeout terminates the process", failures, async () =>
            {
                var executablePath = ResolveTestExecutable();
                await using var client = new RinoSidecarClient(
                    new RinoSidecarLaunchOptions(
                        executablePath,
                        ["--fake-rino-sidecar", "timeout"]),
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromMilliseconds(200),
                    TimeSpan.FromMilliseconds(200));
                var document = JsonSerializer.SerializeToElement(new
                {
                    schemaVersion = 1,
                    documentId = "30000000-0000-4000-8000-000000000001",
                });

                await ExpectProtocolFailureAsync(
                    "sidecar.timeout",
                    () => client.ValidateGraphAsync(document, CancellationToken.None));
            });

            await RunAsync("Rino sidecar exit is reported", failures, async () =>
            {
                await using var client = new RinoSidecarClient(
                    new RinoSidecarLaunchOptions(
                        ResolveTestExecutable(),
                        ["--fake-rino-sidecar", "exit"]),
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromMilliseconds(200));
                var document = JsonSerializer.SerializeToElement(new
                {
                    schemaVersion = 1,
                    documentId = "40000000-0000-4000-8000-000000000001",
                });

                await ExpectProtocolFailureAsync(
                    "sidecar.exited",
                    () => client.ValidateGraphAsync(document, CancellationToken.None));
            });

            await RunAsync("missing bundled Rino sidecar is explicit", failures, async () =>
            {
                var missingExecutable = Path.Combine(
                    testRoot,
                    $"missing-sidecar-{Guid.NewGuid():N}.exe");
                await using var client = new RinoSidecarClient(
                    new RinoSidecarLaunchOptions(missingExecutable, []),
                    TimeSpan.FromMilliseconds(200),
                    TimeSpan.FromMilliseconds(200),
                    TimeSpan.FromMilliseconds(200));

                Assert(!client.IsBundledRuntimePresent, "Expected missing runtime status.");
                await ExpectProtocolFailureAsync(
                    "sidecar.missing",
                    () => client.EnsureStartedAsync(CancellationToken.None));
            });

            await RunAsync("My Scripts configuration is protected", failures, () =>
            {
                Assert(
                    ConfigurationHelper.IsProtectedConfigurationName("My Scripts"),
                    "Expected stable My Scripts configuration name.");
                Assert(
                    ConfigurationHelper.IsProtectedConfigurationName("my scripts"),
                    "Expected case-insensitive protected configuration name.");
                Assert(
                    !ConfigurationHelper.IsProtectedConfigurationName("Default"),
                    "Expected ordinary configurations to remain deletable.");
                var packageConfiguration = new MFAConfiguration
                {
                    Name = "Imported Project",
                    FileName = "Imported Project.json",
                    Config = new Dictionary<string, object>
                    {
                        [ConfigurationHelper.RinoPackageConfigurationIdKey] =
                            "10000000-0000-4000-8000-000000000001",
                    },
                };
                Assert(
                    !packageConfiguration.IsDeletable,
                    "Expected package configurations to require package-aware removal.");
                return Task.CompletedTask;
            });

            await RunAsync("incompatible package requirements rejected", failures, () =>
            {
                var compatibility = new RinoPackageCompatibilityV1
                {
                    MinimumClientVersion = "2.0.0-beta.10",
                    SidecarProtocolVersion = 1,
                    ProjectSchemaVersions = [1],
                    OperatingSystem = "windows",
                    Architectures = ["x86_64"],
                };
                var policy = new RinoPackageCompatibilityPolicy(
                    "2.0.0-beta.2",
                    sidecarProtocolVersion: 1,
                    projectSchemaVersions: [1]);

                ExpectValidationFailure(
                    "compatibility.clientVersion",
                    () => policy.EnsureCompatible(compatibility));
                return Task.CompletedTask;
            });

            await RunAsync("feature package rejects project default", failures, async () =>
            {
                var package = await CreatePackageAsync(
                    testRoot,
                    "feature-default",
                    packageType: "feature",
                    includeDefaultProjectEntrypoint: true);
                await ExpectValidationFailureAsync(
                    "manifest.schema",
                    () => CreateValidator().ValidateAsync(
                        package.ArchivePath,
                        package.ArchiveSha256,
                        package.PublisherKey,
                        CancellationToken.None));
            });

            await RunAsync("duplicate JSON property rejected", failures, () =>
            {
                var bytes = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"schemaVersion\":1}");
                ExpectValidationFailure("manifest.duplicateProperty", () => new RinoPackageManifestReader().Read(bytes));
                return Task.CompletedTask;
            });

            await RunAsync("path traversal rejected", failures, async () =>
            {
                var package = await CreatePackageAsync(
                    testRoot,
                    "traversal",
                    additionalArchiveEntry: "../escape.json");
                await ExpectValidationFailureAsync(
                    "archive.path",
                    () => CreateValidator().ValidateAsync(
                        package.ArchivePath,
                        package.ArchiveSha256,
                        package.PublisherKey,
                        CancellationToken.None));
            });

            await RunAsync("case-colliding archive path rejected", failures, async () =>
            {
                var package = await CreatePackageAsync(
                    testRoot,
                    "case-collision",
                    additionalArchiveEntry: "PAYLOAD/PROJECT.RINO.JSON");
                await ExpectValidationFailureAsync(
                    "archive.duplicatePath",
                    () => CreateValidator().ValidateAsync(
                        package.ArchivePath,
                        package.ArchiveSha256,
                        package.PublisherKey,
                        CancellationToken.None));
            });

            await RunAsync("invalid signature rejected", failures, async () =>
            {
                var package = await CreatePackageAsync(testRoot, "bad-signature", corruptSignature: true);
                await ExpectValidationFailureAsync(
                    "signature.invalid",
                    () => CreateValidator().ValidateAsync(
                        package.ArchivePath,
                        package.ArchiveSha256,
                        package.PublisherKey,
                        CancellationToken.None));
            });

            await RunAsync("payload hash mismatch rejected", failures, async () =>
            {
                var package = await CreatePackageAsync(testRoot, "bad-hash", declareWrongGraphHash: true);
                await ExpectValidationFailureAsync(
                    "archive.fileHash",
                    () => CreateValidator().ValidateAsync(
                        package.ArchivePath,
                        package.ArchiveSha256,
                        package.PublisherKey,
                        CancellationToken.None));
            });

            await RunAsync("catalog archive digest mismatch rejected", failures, async () =>
            {
                var package = await CreatePackageAsync(testRoot, "bad-archive-digest");
                await ExpectValidationFailureAsync(
                    "archive.digest",
                    () => CreateValidator().ValidateAsync(
                        package.ArchivePath,
                        new string('0', 64),
                        package.PublisherKey,
                        CancellationToken.None));
            });

            await RunAsync("valid signed package catalog", failures, () =>
            {
                var fixture = CreateCatalog();
                var result = new RinoPackageCatalogReader().Read(
                    fixture.CatalogBytes,
                    fixture.SignatureBytes,
                    fixture.RootKey,
                    DateTimeOffset.Parse("2026-08-10T01:00:00Z"));

                Assert(!result.IsExpired, "Expected current catalog.");
                Assert(result.Catalog.Packages.Count == 1, "Expected one catalog package.");
                Assert(result.PublisherKeys.ContainsKey("example.publisher.v1"), "Expected publisher key.");
                return Task.CompletedTask;
            });

            await RunAsync("expired signed catalog is marked stale", failures, () =>
            {
                var fixture = CreateCatalog(expiresAt: "2026-08-10T00:30:00Z");
                var result = new RinoPackageCatalogReader().Read(
                    fixture.CatalogBytes,
                    fixture.SignatureBytes,
                    fixture.RootKey,
                    DateTimeOffset.Parse("2026-08-10T01:00:00Z"));

                Assert(result.IsExpired, "Expected expired catalog marker.");
                return Task.CompletedTask;
            });

            await RunAsync("invalid catalog signature rejected", failures, () =>
            {
                var fixture = CreateCatalog(corruptSignature: true);
                ExpectValidationFailure(
                    "catalog.signature",
                    () => new RinoPackageCatalogReader().Read(
                        fixture.CatalogBytes,
                        fixture.SignatureBytes,
                        fixture.RootKey,
                        DateTimeOffset.Parse("2026-08-10T01:00:00Z")));
                return Task.CompletedTask;
            });

            await RunAsync("unknown catalog publisher key rejected", failures, () =>
            {
                var fixture = CreateCatalog(useUnknownPublisherKey: true);
                ExpectValidationFailure(
                    "catalog.packagePublisher",
                    () => new RinoPackageCatalogReader().Read(
                        fixture.CatalogBytes,
                        fixture.SignatureBytes,
                        fixture.RootKey,
                        DateTimeOffset.Parse("2026-08-10T01:00:00Z")));
                return Task.CompletedTask;
            });

            await RunAsync("signed catalog cache reload", failures, async () =>
            {
                var fixture = CreateCatalog();
                var currentTime = DateTimeOffset.Parse("2026-08-10T01:00:00Z");
                var reader = new RinoPackageCatalogReader();
                var validatedCatalog = reader.Read(
                    fixture.CatalogBytes,
                    fixture.SignatureBytes,
                    fixture.RootKey,
                    currentTime);
                var storagePaths = new RinoPackageStoragePaths(
                    Path.Combine(testRoot, $"catalog-cache-{Guid.NewGuid():N}"));
                var cache = new RinoPackageCatalogCache(storagePaths, reader, fixture.RootKey);

                await cache.SaveAsync(
                    new RinoPackageCatalogFetchResult(
                        validatedCatalog,
                        "\"catalog-etag\"",
                        fixture.CatalogBytes,
                        fixture.SignatureBytes),
                    currentTime,
                    CancellationToken.None);
                var cached = await cache.TryLoadAsync(currentTime, CancellationToken.None);

                if (cached is null)
                {
                    throw new InvalidOperationException("Expected saved catalog cache entry.");
                }

                Assert(cached.EntityTag == "\"catalog-etag\"", "Expected cached ETag.");
                Assert(cached.Catalog.Catalog.Packages.Count == 1, "Expected cached catalog package.");
                Assert(cached.FetchedAt == currentTime, "Expected cached fetch time.");
            });

            await RunAsync("tampered catalog cache rejected", failures, async () =>
            {
                var fixture = CreateCatalog();
                var currentTime = DateTimeOffset.Parse("2026-08-10T01:00:00Z");
                var reader = new RinoPackageCatalogReader();
                var validatedCatalog = reader.Read(
                    fixture.CatalogBytes,
                    fixture.SignatureBytes,
                    fixture.RootKey,
                    currentTime);
                var storagePaths = new RinoPackageStoragePaths(
                    Path.Combine(testRoot, $"catalog-cache-tamper-{Guid.NewGuid():N}"));
                var cache = new RinoPackageCatalogCache(storagePaths, reader, fixture.RootKey);
                await cache.SaveAsync(
                    new RinoPackageCatalogFetchResult(
                        validatedCatalog,
                        null,
                        fixture.CatalogBytes,
                        fixture.SignatureBytes),
                    currentTime,
                    CancellationToken.None);
                var catalogPath = Directory.GetFiles(
                    storagePaths.CatalogDirectory,
                    "catalog-*.json").Single();
                var tamperedBytes = await File.ReadAllBytesAsync(catalogPath);
                tamperedBytes[0] ^= 0xff;
                await File.WriteAllBytesAsync(catalogPath, tamperedBytes);

                await ExpectValidationFailureAsync(
                    "catalog.cacheDigest",
                    async () =>
                    {
                        await cache.TryLoadAsync(currentTime, CancellationToken.None);
                    });
            });

            await RunAsync("catalog coordinator reuses cache after 304", failures, async () =>
            {
                var fixture = CreateCatalog();
                var currentTime = DateTimeOffset.Parse("2026-08-10T01:00:00Z");
                var reader = new RinoPackageCatalogReader();
                var validatedCatalog = reader.Read(
                    fixture.CatalogBytes,
                    fixture.SignatureBytes,
                    fixture.RootKey,
                    currentTime);
                var storagePaths = new RinoPackageStoragePaths(
                    Path.Combine(testRoot, $"catalog-coordinator-304-{Guid.NewGuid():N}"));
                var cache = new RinoPackageCatalogCache(storagePaths, reader, fixture.RootKey);
                await cache.SaveAsync(
                    new RinoPackageCatalogFetchResult(
                        validatedCatalog,
                        "\"catalog-etag\"",
                        fixture.CatalogBytes,
                        fixture.SignatureBytes),
                    currentTime,
                    CancellationToken.None);
                using var client = new RinoGitHubReleaseClient(
                    new FakeHttpMessageHandler(request =>
                    {
                        Assert(
                            request.Headers.IfNoneMatch.Single().Tag == "\"catalog-etag\"",
                            "Expected cached ETag request header.");
                        return new HttpResponseMessage(HttpStatusCode.NotModified);
                    }),
                    reader,
                    new RinoGitHubCatalogSource("catalog-owner", "catalog-repository"),
                    fixture.RootKey);
                var coordinator = new RinoPackageCatalogCoordinator(client, cache);

                var result = await coordinator.RefreshAsync(currentTime, CancellationToken.None);

                Assert(
                    result.Source == RinoPackageCatalogRefreshSource.CacheNotModified,
                    "Expected not-modified cache source.");
                Assert(result.RefreshFailure is null, "Expected no refresh failure after 304.");
                Assert(result.Entry.Catalog.Catalog.Packages.Count == 1, "Expected cached package.");
            });

            await RunAsync("catalog coordinator falls back to signed cache offline", failures, async () =>
            {
                var fixture = CreateCatalog();
                var currentTime = DateTimeOffset.Parse("2026-08-10T01:00:00Z");
                var reader = new RinoPackageCatalogReader();
                var validatedCatalog = reader.Read(
                    fixture.CatalogBytes,
                    fixture.SignatureBytes,
                    fixture.RootKey,
                    currentTime);
                var storagePaths = new RinoPackageStoragePaths(
                    Path.Combine(testRoot, $"catalog-coordinator-offline-{Guid.NewGuid():N}"));
                var cache = new RinoPackageCatalogCache(storagePaths, reader, fixture.RootKey);
                await cache.SaveAsync(
                    new RinoPackageCatalogFetchResult(
                        validatedCatalog,
                        "\"catalog-etag\"",
                        fixture.CatalogBytes,
                        fixture.SignatureBytes),
                    currentTime,
                    CancellationToken.None);
                using var client = new RinoGitHubReleaseClient(
                    new FakeHttpMessageHandler(_ => throw new HttpRequestException("offline")),
                    reader,
                    new RinoGitHubCatalogSource("catalog-owner", "catalog-repository"),
                    fixture.RootKey);
                var coordinator = new RinoPackageCatalogCoordinator(client, cache);

                var result = await coordinator.RefreshAsync(currentTime, CancellationToken.None);

                Assert(
                    result.Source == RinoPackageCatalogRefreshSource.CacheAfterRefreshFailure,
                    "Expected offline cache source.");
                Assert(result.RefreshFailure?.Code == "network.request", "Expected network failure detail.");
                Assert(result.Entry.Catalog.Catalog.Packages.Count == 1, "Expected cached package.");
            });

            await RunAsync("GitHub catalog fetch follows approved redirect", failures, async () =>
            {
                var fixture = CreateCatalog();
                var releaseBytes = CreateReleaseMetadata(
                    fixture.CatalogBytes.LongLength,
                    fixture.SignatureBytes.LongLength);
                using var client = new RinoGitHubReleaseClient(
                    new FakeHttpMessageHandler(request =>
                    {
                        AssertGitHubHeaders(request);
                        var path = request.RequestUri?.AbsolutePath;
                        return path switch
                        {
                            "/repos/catalog-owner/catalog-repository/releases/latest" =>
                                CreateByteResponse(releaseBytes, "\"catalog-etag\""),
                            "/repos/catalog-owner/catalog-repository/releases/assets/1" =>
                                CreateRedirectResponse(
                                    "https://release-assets.githubusercontent.com/catalog-v1.json"),
                            "/catalog-v1.json" => CreateByteResponse(fixture.CatalogBytes),
                            "/repos/catalog-owner/catalog-repository/releases/assets/2" =>
                                CreateByteResponse(fixture.SignatureBytes),
                            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
                        };
                    }),
                    new RinoPackageCatalogReader(),
                    new RinoGitHubCatalogSource("catalog-owner", "catalog-repository"),
                    fixture.RootKey);

                var result = await client.FetchCatalogAsync(
                    null,
                    DateTimeOffset.Parse("2026-08-10T01:00:00Z"),
                    CancellationToken.None);
                Assert(result.Catalog.Catalog.Packages.Count == 1, "Expected fetched catalog package.");
                Assert(result.EntityTag == "\"catalog-etag\"", "Expected release ETag.");
            });

            await RunAsync("GitHub redirect to unapproved host rejected", failures, async () =>
            {
                var fixture = CreateCatalog();
                using var client = new RinoGitHubReleaseClient(
                    new FakeHttpMessageHandler(_ =>
                        CreateRedirectResponse("https://example.invalid/catalog-v1.json")),
                    new RinoPackageCatalogReader(),
                    new RinoGitHubCatalogSource("catalog-owner", "catalog-repository"),
                    fixture.RootKey);

                await ExpectTransportFailureAsync(
                    "network.redirectHost",
                    () => client.FetchCatalogAsync(
                        null,
                        DateTimeOffset.Parse("2026-08-10T01:00:00Z"),
                        CancellationToken.None));
            });

            await RunAsync("GitHub rate limit exposes retry time", failures, async () =>
            {
                var fixture = CreateCatalog();
                var expectedRetryAt = DateTimeOffset.Parse("2026-08-10T03:00:00Z");
                using var client = new RinoGitHubReleaseClient(
                    new FakeHttpMessageHandler(_ =>
                    {
                        var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
                        response.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", "0");
                        response.Headers.TryAddWithoutValidation(
                            "X-RateLimit-Reset",
                            expectedRetryAt.ToUnixTimeSeconds().ToString());
                        return response;
                    }),
                    new RinoPackageCatalogReader(),
                    new RinoGitHubCatalogSource("catalog-owner", "catalog-repository"),
                    fixture.RootKey);

                try
                {
                    await client.FetchCatalogAsync(
                        null,
                        DateTimeOffset.Parse("2026-08-10T01:00:00Z"),
                        CancellationToken.None);
                }
                catch (RinoPackageTransportException exception)
                    when (exception.Code == "network.rateLimit")
                {
                    Assert(exception.RetryAt == expectedRetryAt, "Expected GitHub reset time.");
                    return;
                }

                throw new InvalidOperationException("Expected GitHub rate-limit failure.");
            });

            await RunAsync("bounded package asset download", failures, async () =>
            {
                var packageBytes = Encoding.UTF8.GetBytes("signed package payload");
                var fixture = CreateCatalog(packageBytes: packageBytes);
                var catalog = new RinoPackageCatalogReader().Read(
                    fixture.CatalogBytes,
                    fixture.SignatureBytes,
                    fixture.RootKey,
                    DateTimeOffset.Parse("2026-08-10T01:00:00Z"));
                using var client = new RinoGitHubReleaseClient(
                    new FakeHttpMessageHandler(_ => CreateByteResponse(packageBytes)),
                    new RinoPackageCatalogReader(),
                    new RinoGitHubCatalogSource("catalog-owner", "catalog-repository"),
                    fixture.RootKey);
                await using var destination = new MemoryStream();

                await client.DownloadPackageAsync(
                    catalog.Catalog.Packages[0],
                    destination,
                    progress: null,
                    CancellationToken.None);

                Assert(destination.ToArray().SequenceEqual(packageBytes), "Expected exact package bytes.");
            });

            await RunAsync("package asset digest mismatch rejected", failures, async () =>
            {
                var expectedBytes = Encoding.UTF8.GetBytes("signed package payload");
                var actualBytes = Encoding.UTF8.GetBytes("tamper package payload");
                Assert(expectedBytes.Length == actualBytes.Length, "Digest fixture lengths must match.");
                var fixture = CreateCatalog(packageBytes: expectedBytes);
                var catalog = new RinoPackageCatalogReader().Read(
                    fixture.CatalogBytes,
                    fixture.SignatureBytes,
                    fixture.RootKey,
                    DateTimeOffset.Parse("2026-08-10T01:00:00Z"));
                using var client = new RinoGitHubReleaseClient(
                    new FakeHttpMessageHandler(_ => CreateByteResponse(actualBytes)),
                    new RinoPackageCatalogReader(),
                    new RinoGitHubCatalogSource("catalog-owner", "catalog-repository"),
                    fixture.RootKey);
                await using var destination = new MemoryStream();

                await ExpectTransportFailureAsync(
                    "download.digest",
                    () => client.DownloadPackageAsync(
                        catalog.Catalog.Packages[0],
                        destination,
                        progress: null,
                        CancellationToken.None));
            });

            await RunAsync("package installs through same-volume atomic staging", failures, async () =>
            {
                var packageFixture = await CreatePackageAsync(testRoot, "install-valid");
                var packageBytes = await File.ReadAllBytesAsync(packageFixture.ArchivePath);
                var catalogFixture = CreateCatalog(
                    packageBytes: packageBytes,
                    publisherSigningKey: packageFixture.PublisherKey);
                var catalog = new RinoPackageCatalogReader().Read(
                    catalogFixture.CatalogBytes,
                    catalogFixture.SignatureBytes,
                    catalogFixture.RootKey,
                    DateTimeOffset.Parse("2026-08-10T01:00:00Z"));
                using var releaseClient = new RinoGitHubReleaseClient(
                    new FakeHttpMessageHandler(_ => CreateByteResponse(packageBytes)),
                    new RinoPackageCatalogReader(),
                    new RinoGitHubCatalogSource("catalog-owner", "catalog-repository"),
                    catalogFixture.RootKey);
                var storageRoot = Path.Combine(testRoot, $"store-{Guid.NewGuid():N}");
                var storagePaths = new RinoPackageStoragePaths(storageRoot);
                var installer = new RinoPackageInstallationService(
                    releaseClient,
                    CreateValidator(),
                    storagePaths,
                    CreateCompatibilityPolicy());

                var result = await installer.InstallAsync(
                    catalog,
                    "example.package",
                    "1.0.0",
                    progress: null,
                    CancellationToken.None);

                Assert(Directory.Exists(result.InstalledDirectory), "Expected installed directory.");
                Assert(
                    File.Exists(Path.Combine(result.InstalledDirectory, "package.rino.json")),
                    "Expected installed package manifest.");
                Assert(
                    File.Exists(Path.Combine(
                        result.InstalledDirectory,
                        "payload",
                        "graphs",
                        "main.rino.graph.json")),
                    "Expected installed graph.");
                Assert(
                    !Directory.EnumerateFileSystemEntries(storagePaths.DownloadsDirectory).Any(),
                    "Expected download staging cleanup.");
                Assert(
                    !Directory.EnumerateFileSystemEntries(storagePaths.StagingDirectory).Any(),
                    "Expected extraction staging cleanup.");

                await ExpectValidationFailureAsync(
                    "install.exists",
                    () => installer.InstallAsync(
                        catalog,
                        "example.package",
                        "1.0.0",
                        progress: null,
                        CancellationToken.None));
            });

            await RunAsync("project import creates one stable configuration", failures, async () =>
            {
                var fixture = await InstallPackageForImportAsync(
                    testRoot,
                    "import-project",
                    packageType: "project",
                    includeDefaultProjectEntrypoint: true);
                var stateStore = new RinoPackageImportStateStore(fixture.StoragePaths);
                var configurationDirectory = Path.Combine(
                    testRoot,
                    $"config-{Guid.NewGuid():N}");
                var importService = new RinoPackageImportService(
                    new RinoInstalledPackageVerifier(
                        fixture.StoragePaths,
                        new RinoPackageManifestReader(),
                        CreateCompatibilityPolicy()),
                    stateStore,
                    configurationDirectory);

                var first = await importService.ImportProjectAsync(
                    fixture.Catalog,
                    "example.package",
                    "1.0.0",
                    "Example Project",
                    CancellationToken.None);
                var second = await importService.ImportProjectAsync(
                    fixture.Catalog,
                    "example.package",
                    "1.0.0",
                    "Ignored Duplicate Name",
                    CancellationToken.None);
                var state = await importService.ReadStateAsync(CancellationToken.None);

                Assert(!first.AlreadyImported, "Expected first project import to create a binding.");
                Assert(second.AlreadyImported, "Expected repeated project import to be idempotent.");
                Assert(
                    first.Binding.ConfigurationId == second.Binding.ConfigurationId,
                    "Expected repeated import to retain the stable configuration ID.");
                Assert(state.ProjectImports.Count == 1, "Expected one project binding.");
                Assert(
                    Directory.GetFiles(configurationDirectory, "*.json").Length == 1,
                    "Expected one package configuration file.");
                var configurationJson = await File.ReadAllTextAsync(Path.Combine(
                    configurationDirectory,
                    $"{first.Binding.ConfigurationName}.json"));
                Assert(
                    configurationJson.Contains(
                        ConfigurationHelper.RinoPackageConfigurationIdKey,
                        StringComparison.Ordinal),
                    "Expected a stable package configuration marker.");
            });

            await RunAsync("feature import binds idempotently to My Scripts", failures, async () =>
            {
                var fixture = await InstallPackageForImportAsync(
                    testRoot,
                    "import-feature",
                    packageType: "feature",
                    includeDefaultProjectEntrypoint: false);
                var stateStore = new RinoPackageImportStateStore(fixture.StoragePaths);
                var importService = new RinoPackageImportService(
                    new RinoInstalledPackageVerifier(
                        fixture.StoragePaths,
                        new RinoPackageManifestReader(),
                        CreateCompatibilityPolicy()),
                    stateStore,
                    Path.Combine(testRoot, $"config-{Guid.NewGuid():N}"));

                var first = await importService.ImportFeatureAsync(
                    fixture.Catalog,
                    "example.package",
                    "1.0.0",
                    "main",
                    CancellationToken.None);
                var second = await importService.ImportFeatureAsync(
                    fixture.Catalog,
                    "example.package",
                    "1.0.0",
                    "main",
                    CancellationToken.None);
                var state = await importService.ReadStateAsync(CancellationToken.None);

                Assert(!first.AlreadyImported, "Expected first feature import to create a binding.");
                Assert(second.AlreadyImported, "Expected repeated feature import to be idempotent.");
                Assert(state.FeatureImports.Count == 1, "Expected one feature binding.");
                Assert(
                    state.FeatureImports[0].ConfigurationId
                        == RinoPackageImportStateStore.MyScriptsConfigurationId,
                    "Expected feature binding to target My Scripts.");
            });

            await RunAsync("tampered installed package cannot be imported", failures, async () =>
            {
                var fixture = await InstallPackageForImportAsync(
                    testRoot,
                    "import-tampered",
                    packageType: "project",
                    includeDefaultProjectEntrypoint: true);
                var graphPath = Path.Combine(
                    fixture.StoragePaths.GetPackageVersionDirectory("example.package", "1.0.0"),
                    "payload",
                    "graphs",
                    "main.rino.graph.json");
                await File.AppendAllTextAsync(graphPath, " ");
                var importService = new RinoPackageImportService(
                    new RinoInstalledPackageVerifier(
                        fixture.StoragePaths,
                        new RinoPackageManifestReader(),
                        CreateCompatibilityPolicy()),
                    new RinoPackageImportStateStore(fixture.StoragePaths),
                    Path.Combine(testRoot, $"config-{Guid.NewGuid():N}"));

                await ExpectValidationFailureAsync(
                    "import.fileSize",
                    () => importService.ImportProjectAsync(
                        fixture.Catalog,
                        "example.package",
                        "1.0.0",
                        "Tampered",
                        CancellationToken.None));
            });

            await RunAsync("installed package assembles and passes Sidecar preflight", failures, async () =>
            {
                var fixture = await InstallPackageForImportAsync(
                    testRoot,
                    "sidecar-preflight",
                    packageType: "project",
                    includeDefaultProjectEntrypoint: true);
                var verifier = new RinoInstalledPackageVerifier(
                    fixture.StoragePaths,
                    new RinoPackageManifestReader(),
                    CreateCompatibilityPolicy());
                await using var client = new RinoSidecarClient(
                    new RinoSidecarLaunchOptions(
                        ResolveTestExecutable(),
                        ["--fake-rino-sidecar", "normal"]),
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(1));
                var preflight = new RinoPackagePreflightService(
                    verifier,
                    new RinoProjectDocumentAssembler(),
                    client);

                var result = await preflight.PreflightAsync(
                    fixture.Catalog,
                    "example.package",
                    "1.0.0",
                    "main",
                    CancellationToken.None);

                Assert(result.Executable, "Expected installed package to pass Sidecar preflight.");
            });

            await RunAsync("incompatible package rejected before download", failures, async () =>
            {
                var catalogFixture = CreateCatalog(minimumClientVersion: "2.0.0");
                var catalog = new RinoPackageCatalogReader().Read(
                    catalogFixture.CatalogBytes,
                    catalogFixture.SignatureBytes,
                    catalogFixture.RootKey,
                    DateTimeOffset.Parse("2026-08-10T01:00:00Z"));
                using var releaseClient = new RinoGitHubReleaseClient(
                    new FakeHttpMessageHandler(_ =>
                        throw new InvalidOperationException("Download must not start.")),
                    new RinoPackageCatalogReader(),
                    new RinoGitHubCatalogSource("catalog-owner", "catalog-repository"),
                    catalogFixture.RootKey);
                var installer = new RinoPackageInstallationService(
                    releaseClient,
                    CreateValidator(),
                    new RinoPackageStoragePaths(
                        Path.Combine(testRoot, $"store-incompatible-{Guid.NewGuid():N}")),
                    CreateCompatibilityPolicy());

                await ExpectValidationFailureAsync(
                    "compatibility.clientVersion",
                    () => installer.InstallAsync(
                        catalog,
                        "example.package",
                        "1.0.0",
                        progress: null,
                        CancellationToken.None));
            });

            await RunAsync("catalog and package identity mismatch rejected", failures, async () =>
            {
                var packageFixture = await CreatePackageAsync(testRoot, "install-binding-mismatch");
                var packageBytes = await File.ReadAllBytesAsync(packageFixture.ArchivePath);
                var catalogFixture = CreateCatalog(
                    packageBytes: packageBytes,
                    publisherSigningKey: packageFixture.PublisherKey,
                    catalogPackageId: "different.package");
                var catalog = new RinoPackageCatalogReader().Read(
                    catalogFixture.CatalogBytes,
                    catalogFixture.SignatureBytes,
                    catalogFixture.RootKey,
                    DateTimeOffset.Parse("2026-08-10T01:00:00Z"));
                using var releaseClient = new RinoGitHubReleaseClient(
                    new FakeHttpMessageHandler(_ => CreateByteResponse(packageBytes)),
                    new RinoPackageCatalogReader(),
                    new RinoGitHubCatalogSource("catalog-owner", "catalog-repository"),
                    catalogFixture.RootKey);
                var installer = new RinoPackageInstallationService(
                    releaseClient,
                    CreateValidator(),
                    new RinoPackageStoragePaths(Path.Combine(testRoot, $"store-{Guid.NewGuid():N}")),
                    CreateCompatibilityPolicy());

                await ExpectValidationFailureAsync(
                    "install.catalogBinding",
                    () => installer.InstallAsync(
                        catalog,
                        "different.package",
                        "1.0.0",
                        progress: null,
                        CancellationToken.None));
            });
        }
        finally
        {
            var resolvedRoot = Path.GetFullPath(testRoot);
            var resolvedTemp = Path.GetFullPath(Path.GetTempPath());
            if (!resolvedRoot.StartsWith(resolvedTemp, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(resolvedRoot).StartsWith(
                    "rino-package-contract-tests-",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Refusing to remove unexpected test directory: {resolvedRoot}");
            }

            Directory.Delete(resolvedRoot, recursive: true);
        }

        if (failures.Count == 0)
        {
            Console.WriteLine("All Rino package contract tests passed.");
            return 0;
        }

        Console.Error.WriteLine($"{failures.Count} Rino package contract test(s) failed:");
        foreach (var failure in failures)
        {
            Console.Error.WriteLine($"- {failure}");
        }

        return 1;
    }

    private static RinoPackageArchiveValidator CreateValidator() =>
        new(new RinoPackageManifestReader());

    private static RinoPackageCompatibilityPolicy CreateCompatibilityPolicy() =>
        new("1.5.0", sidecarProtocolVersion: 1, projectSchemaVersions: [1]);

    private static async Task<ImportFixture> InstallPackageForImportAsync(
        string testRoot,
        string fixtureName,
        string packageType,
        bool includeDefaultProjectEntrypoint)
    {
        var packageFixture = await CreatePackageAsync(
            testRoot,
            fixtureName,
            packageType,
            includeDefaultProjectEntrypoint);
        var packageBytes = await File.ReadAllBytesAsync(packageFixture.ArchivePath);
        var catalogFixture = CreateCatalog(
            packageBytes: packageBytes,
            publisherSigningKey: packageFixture.PublisherKey,
            packageType: packageType);
        var catalog = new RinoPackageCatalogReader().Read(
            catalogFixture.CatalogBytes,
            catalogFixture.SignatureBytes,
            catalogFixture.RootKey,
            DateTimeOffset.Parse("2026-08-10T01:00:00Z"));
        using var releaseClient = new RinoGitHubReleaseClient(
            new FakeHttpMessageHandler(_ => CreateByteResponse(packageBytes)),
            new RinoPackageCatalogReader(),
            new RinoGitHubCatalogSource("catalog-owner", "catalog-repository"),
            catalogFixture.RootKey);
        var storagePaths = new RinoPackageStoragePaths(
            Path.Combine(testRoot, $"store-{fixtureName}-{Guid.NewGuid():N}"));
        var installer = new RinoPackageInstallationService(
            releaseClient,
            CreateValidator(),
            storagePaths,
            CreateCompatibilityPolicy());
        await installer.InstallAsync(
            catalog,
            "example.package",
            "1.0.0",
            progress: null,
            CancellationToken.None);
        return new ImportFixture(catalog, storagePaths);
    }

    private static string ResolveTestExecutable()
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "MFAWPF.PackageTests.exe");
        if (!File.Exists(executable))
        {
            throw new InvalidOperationException("The package-test app host executable is unavailable.");
        }

        return executable;
    }

    private static async Task<int> RunFakeRinoSidecarAsync(string mode)
    {
        await using var input = Console.OpenStandardInput();
        await using var output = Console.OpenStandardOutput();
        long sequence = 0;
        while (true)
        {
            var request = await RinoSidecarProtocol.ReadEnvelopeAsync(
                input,
                RinoSidecarProtocol.MaximumSupportedFrameBytes,
                CancellationToken.None);
            if (request is null)
            {
                return 0;
            }

            if (request.MessageKind != "request" || request.RequestId is null)
            {
                return 2;
            }

            if (request.MessageType == "system.handshake")
            {
                await WriteFakeSidecarEnvelopeAsync(
                    output,
                    new
                    {
                        protocolVersion = 1,
                        messageKind = "response",
                        messageType = request.MessageType,
                        requestId = request.RequestId,
                        result = new
                        {
                            runtimeVersion = "1.0.0",
                            protocolVersion = 1,
                            maximumFrameBytes = RinoSidecarProtocol.MaximumSupportedFrameBytes,
                            runtimeMode = "frozen",
                            graphSchemaVersionRange = new { minimum = 1, maximum = 1 },
                            registryVersion = new string('0', 64),
                            maaRuntime = new { state = "unavailable" },
                            featureFlags = Array.Empty<string>(),
                        },
                    });
                await WriteFakeSidecarEnvelopeAsync(
                    output,
                    new
                    {
                        protocolVersion = 1,
                        messageKind = "event",
                        messageType = "system.ready",
                        eventId = Guid.NewGuid().ToString("D"),
                        sequence = sequence++,
                        payload = new { state = "ready" },
                    });
                continue;
            }

            if (request.MessageType == "graph.validate")
            {
                if (mode == "exit")
                {
                    return 0;
                }

                if (mode == "timeout")
                {
                    await Task.Delay(TimeSpan.FromMinutes(1));
                    return 0;
                }

                await WriteFakeSidecarEnvelopeAsync(
                    output,
                    new
                    {
                        protocolVersion = 1,
                        messageKind = "response",
                        messageType = request.MessageType,
                        requestId = request.RequestId,
                        result = new
                        {
                            executable = true,
                            report = new
                            {
                                schemaVersion = 1,
                                diagnostics = Array.Empty<object>(),
                            },
                        },
                    });
                continue;
            }

            if (request.MessageType == "system.shutdown")
            {
                await WriteFakeSidecarEnvelopeAsync(
                    output,
                    new
                    {
                        protocolVersion = 1,
                        messageKind = "response",
                        messageType = request.MessageType,
                        requestId = request.RequestId,
                        result = new { accepted = true },
                    });
                return 0;
            }

            return 3;
        }
    }

    private static async Task WriteFakeSidecarEnvelopeAsync(Stream output, object envelope)
    {
        var frame = RinoSidecarProtocol.EncodeEnvelopeForTesting(
            envelope,
            RinoSidecarProtocol.MaximumSupportedFrameBytes);
        await output.WriteAsync(frame);
        await output.FlushAsync();
    }

    private static async Task RunAsync(
        string name,
        ICollection<string> failures,
        Func<Task> test)
    {
        try
        {
            await test();
            Console.WriteLine($"PASS: {name}");
        }
        catch (Exception exception)
        {
            failures.Add($"{name}: {exception}");
        }
    }

    private static async Task<TestPackage> CreatePackageAsync(
        string testRoot,
        string name,
        string packageType = "project",
        bool includeDefaultProjectEntrypoint = true,
        string? additionalArchiveEntry = null,
        bool corruptSignature = false,
        bool declareWrongGraphHash = false)
    {
        const string documentId = "10000000-0000-4000-8000-000000000001";
        const string graphId = "10000000-0000-4000-8000-000000000002";
        var projectBytes = JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                schemaVersion = 1,
                documentId,
                metadata = new
                {
                    name = "Package fixture",
                    createdAt = "2026-08-10T00:00:00Z",
                    updatedAt = "2026-08-10T00:00:00Z",
                },
                entryGraphId = graphId,
                graphs = new[]
                {
                    new
                    {
                        graphId,
                        fileName = "main.rino.graph.json",
                    },
                },
                assets = Array.Empty<object>(),
                requiredCapabilities = new[] { "automation.captureScreen" },
            },
            JsonOptions);
        var graphBytes = JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                schemaVersion = 1,
                documentId,
                graph = new
                {
                    graphId,
                    name = "Main",
                    kind = "entry",
                    nodes = Array.Empty<object>(),
                    edges = Array.Empty<object>(),
                },
            },
            JsonOptions);
        var projectHash = ComputeSha256(projectBytes);
        var graphHash = declareWrongGraphHash ? new string('0', 64) : ComputeSha256(graphBytes);

        var payload = new Dictionary<string, object?>
        {
            ["projectManifestPath"] = "payload/project.rino.json",
            ["entrypoints"] = new[]
            {
                new
                {
                    entrypointId = "main",
                    graphId,
                    displayName = new { @default = "Main" },
                    requiredCapabilities = new[] { "automation.captureScreen" },
                },
            },
        };
        if (includeDefaultProjectEntrypoint)
        {
            payload["defaultProjectEntrypointId"] = "main";
        }

        var manifest = new
        {
            schemaVersion = 1,
            packageId = "example.package",
            version = "1.0.0",
            packageType,
            metadata = new
            {
                displayName = new { @default = "Example Package" },
                summary = new { @default = "Package contract fixture" },
                tags = new[] { "test" },
            },
            publisher = new
            {
                publisherId = "example.publisher",
                displayName = "Example Publisher",
                homepage = "https://example.com",
            },
            license = new { identifier = "MIT" },
            source = new
            {
                provider = "github",
                owner = "example",
                repository = "package",
                releaseTag = "v1.0.0",
                assetName = "example-package.rino-package",
                repositoryUrl = "https://github.com/example/package",
            },
            releasedAt = "2026-08-10T00:00:00Z",
            compatibility = new
            {
                minimumClientVersion = "1.0.0",
                sidecarProtocolVersion = 1,
                projectSchemaVersions = new[] { 1 },
                operatingSystem = "windows",
                architectures = new[] { "x86_64" },
            },
            requiredCapabilities = new[] { "automation.captureScreen" },
            payload,
            files = new object[]
            {
                new
                {
                    path = "payload/project.rino.json",
                    role = "projectManifest",
                    mediaType = "application/json",
                    byteLength = projectBytes.LongLength,
                    sha256 = projectHash,
                },
                new
                {
                    path = "payload/graphs/main.rino.graph.json",
                    role = "graph",
                    mediaType = "application/json",
                    byteLength = graphBytes.LongLength,
                    sha256 = graphHash,
                },
            },
            signing = new
            {
                algorithm = "ed25519",
                keyId = "example.publisher.v1",
                signatureFile = "signature.ed25519",
            },
        };

        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
        var privateKey = new Ed25519PrivateKeyParameters(new SecureRandom());
        var publicKey = privateKey.GeneratePublicKey().GetEncoded();
        var signer = new Ed25519Signer();
        signer.Init(true, privateKey);
        signer.BlockUpdate(manifestBytes, 0, manifestBytes.Length);
        var signature = signer.GenerateSignature();
        if (corruptSignature)
        {
            signature[0] ^= 0x01;
        }

        var archivePath = Path.Combine(testRoot, $"{name}.rino-package");
        await using (var fileStream = new FileStream(
            archivePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            64 * 1024,
            FileOptions.Asynchronous))
        using (var archive = new ZipArchive(fileStream, ZipArchiveMode.Create, leaveOpen: false))
        {
            await WriteEntryAsync(archive, "package.rino.json", manifestBytes);
            await WriteEntryAsync(archive, "signature.ed25519", signature);
            await WriteEntryAsync(archive, "payload/project.rino.json", projectBytes);
            await WriteEntryAsync(archive, "payload/graphs/main.rino.graph.json", graphBytes);
            if (additionalArchiveEntry is not null)
            {
                await WriteEntryAsync(archive, additionalArchiveEntry, Encoding.UTF8.GetBytes("{}"));
            }
        }

        return new TestPackage(
            archivePath,
            ComputeSha256(await File.ReadAllBytesAsync(archivePath)),
            new RinoPackageSigningPublicKey("example.publisher.v1", publicKey));
    }

    private static TestCatalog CreateCatalog(
        string expiresAt = "2026-08-11T00:00:00Z",
        bool corruptSignature = false,
        bool useUnknownPublisherKey = false,
        byte[]? packageBytes = null,
        RinoPackageSigningPublicKey? publisherSigningKey = null,
        string catalogPackageId = "example.package",
        string minimumClientVersion = "1.0.0",
        string packageType = "project")
    {
        var rootPrivateKey = new Ed25519PrivateKeyParameters(new SecureRandom());
        var rootPublicKey = rootPrivateKey.GeneratePublicKey().GetEncoded();
        var publisherPublicKey = publisherSigningKey?.PublicKeyBytes.ToArray()
            ?? new Ed25519PrivateKeyParameters(new SecureRandom()).GeneratePublicKey().GetEncoded();

        var catalog = new
        {
            schemaVersion = 1,
            generatedAt = "2026-08-10T00:00:00Z",
            expiresAt,
            publisherKeys = new[]
            {
                new
                {
                    publisherId = "example.publisher",
                    publisherName = "Example Publisher",
                    keyId = "example.publisher.v1",
                    algorithm = "ed25519",
                    publicKeyBase64 = Convert.ToBase64String(publisherPublicKey),
                },
            },
            packages = new[]
            {
                new
                {
                    packageId = catalogPackageId,
                    version = "1.0.0",
                    packageType,
                    displayName = new { @default = "Example Package" },
                    summary = new { @default = "Catalog fixture" },
                    tags = new[] { "test" },
                    publisherKeyId = useUnknownPublisherKey
                        ? "unknown.publisher.v1"
                        : "example.publisher.v1",
                    owner = "example",
                    repository = "package",
                    releaseTag = "v1.0.0",
                    releaseAssetId = 123456789L,
                    assetName = "example-package.rino-package",
                    byteLength = packageBytes?.LongLength ?? 4096L,
                    sha256 = packageBytes is null ? new string('a', 64) : ComputeSha256(packageBytes),
                    releasedAt = "2026-08-10T00:00:00Z",
                    compatibility = new
                    {
                        minimumClientVersion,
                        sidecarProtocolVersion = 1,
                        projectSchemaVersions = new[] { 1 },
                        operatingSystem = "windows",
                        architectures = new[] { "x86_64" },
                    },
                },
            },
            signing = new
            {
                algorithm = "ed25519",
                keyId = "catalog.root.v1",
                signatureFile = "catalog-v1.sig",
            },
        };

        var catalogBytes = JsonSerializer.SerializeToUtf8Bytes(catalog, JsonOptions);
        var signer = new Ed25519Signer();
        signer.Init(true, rootPrivateKey);
        signer.BlockUpdate(catalogBytes, 0, catalogBytes.Length);
        var signatureBytes = signer.GenerateSignature();
        if (corruptSignature)
        {
            signatureBytes[0] ^= 0x01;
        }

        return new TestCatalog(
            catalogBytes,
            signatureBytes,
            new RinoPackageSigningPublicKey("catalog.root.v1", rootPublicKey));
    }

    private static byte[] CreateReleaseMetadata(long catalogBytes, long signatureBytes) =>
        JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                draft = false,
                prerelease = false,
                assets = new object[]
                {
                    new
                    {
                        id = 1L,
                        name = "catalog-v1.json",
                        state = "uploaded",
                        size = catalogBytes,
                    },
                    new
                    {
                        id = 2L,
                        name = "catalog-v1.sig",
                        state = "uploaded",
                        size = signatureBytes,
                    },
                },
            },
            JsonOptions);

    private static HttpResponseMessage CreateByteResponse(byte[] bytes, string? entityTag = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes),
        };
        if (entityTag is not null)
        {
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(entityTag);
        }

        return response;
    }

    private static HttpResponseMessage CreateRedirectResponse(string location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri(location, UriKind.Absolute);
        return response;
    }

    private static void AssertGitHubHeaders(HttpRequestMessage request)
    {
        Assert(
            request.Headers.TryGetValues("X-GitHub-Api-Version", out var versions)
            && versions.Single() == "2026-03-10",
            "Expected pinned GitHub API version.");
        Assert(request.Headers.UserAgent.Count > 0, "Expected product user agent.");
    }

    private static async Task WriteEntryAsync(ZipArchive archive, string path, byte[] bytes)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await stream.WriteAsync(bytes);
    }

    private static string ComputeSha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static void ExpectValidationFailure(string expectedCode, Action action)
    {
        try
        {
            action();
        }
        catch (RinoPackageValidationException exception) when (exception.Code == expectedCode)
        {
            return;
        }

        throw new InvalidOperationException($"Expected validation failure '{expectedCode}'.");
    }

    private static async Task ExpectValidationFailureAsync(
        string expectedCode,
        Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (RinoPackageValidationException exception) when (exception.Code == expectedCode)
        {
            return;
        }

        throw new InvalidOperationException($"Expected validation failure '{expectedCode}'.");
    }

    private static async Task ExpectTransportFailureAsync(
        string expectedCode,
        Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (RinoPackageTransportException exception) when (exception.Code == expectedCode)
        {
            return;
        }

        throw new InvalidOperationException($"Expected transport failure '{expectedCode}'.");
    }

    private static async Task ExpectProtocolFailureAsync(
        string expectedCode,
        Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (RinoSidecarProtocolException exception) when (exception.Code == expectedCode)
        {
            return;
        }

        throw new InvalidOperationException($"Expected Sidecar protocol failure '{expectedCode}'.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed record TestPackage(
        string ArchivePath,
        string ArchiveSha256,
        RinoPackageSigningPublicKey PublisherKey);

    private sealed record TestCatalog(
        byte[] CatalogBytes,
        byte[] SignatureBytes,
        RinoPackageSigningPublicKey RootKey);

    private sealed record ImportFixture(
        ValidatedRinoPackageCatalog Catalog,
        RinoPackageStoragePaths StoragePaths);

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_responseFactory(request));
        }
    }
}
