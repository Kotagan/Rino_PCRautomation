# Rino Package Integration V1

- Status: client download/import and runtime preflight implemented; execution activation pending
- Applies to: the Rino authoring application and the Rino packaged client
- Normative package schema: [Rino Package Manifest V1](contracts/rino-package-v1.schema.json)

## 1. Purpose

This document defines how an automation project authored in Rino becomes a reviewed,
downloadable package that the packaged client can install and execute. It also defines how
the client represents a complete-project import and an individual-feature import without
merging untrusted files into its application directory.

The V1 decisions are fixed:

- Discovery uses one centrally curated catalog of approved public GitHub Release packages.
  Users cannot add arbitrary repository URLs.
- Packages use the native, versioned `.rino-package` format and execute through the Rino
  Python Sidecar. The integration does not translate Rino graphs into Maa `interface.json`
  or Maa Pipeline.
- The publisher declares the package type as `project`, `feature`, or `hybrid`. The client
  offers only the import modes that type permits.
- A complete project creates its own client configuration. Individual features are added by
  stable references to the reserved, non-deletable `My Scripts` configuration.

V1 is a download and package-management feature, not a marketplace. It has no accounts,
ratings, comments, payments, remote execution, telemetry, or arbitrary plugin execution.

## 2. Existing boundaries

Rino authoring projects are directories containing `project.rino.json`, graph documents,
and content-addressed PNG image objects. The authoring project format remains the source
format. A `.rino-package` is a distribution envelope around a validated project subset; it
does not replace the authoring format.

The current packaged client loads Maa resources and configuration state from its application
directory. Native Rino packages are intentionally separate from that legacy path. New code
must introduce a package store and a Python Sidecar session boundary instead of copying files
into `Resource`, rewriting the root `interface.json`, or executing graphs in WPF.

The Python Sidecar remains the only Rino graph executor. The WPF client owns UI, approved
catalog access, bounded download transport, package storage, configuration references,
process lifecycle, and structured IPC. Both the client and Sidecar validate every package and
project boundary they consume.

## 3. Responsibilities

| Concern | Rino authoring application | Packaged client | Python Sidecar |
| --- | --- | --- | --- |
| Project editing | Owns | Does not edit | Does not edit |
| Package construction | Produces deterministic package bytes | Does not construct publisher packages | Does not construct packages |
| Catalog approval | Does not discover or publish remotely in V1 | Consumes the approved catalog | No access |
| Download and installation | No access | Owns | No access |
| Package validation | Before export | Before and after extraction | Before load |
| Configuration binding | No access | Owns stable package and entry-point references | Receives resolved runtime input |
| Graph execution | No access | Never executes | Sole executor |
| Device operations | No access in the package layer | Coordinates lifecycle only | Owns runtime device operations |

The Rino authoring application exports a local file only. Uploading the file to GitHub and
adding it to the curated catalog remain an explicit publisher/reviewer workflow in V1.

## 4. Distribution archive

`.rino-package` is a ZIP container with a fixed root layout:

```text
example-1.2.3.rino-package
├─ package.rino.json
├─ signature.ed25519
├─ payload/
│  ├─ project.rino.json
│  ├─ graphs/
│  │  ├─ main.rino.graph.json
│  │  └─ graph-1.rino.graph.json
│  └─ assets/
│     └─ images/
│        └─ <lowercase-sha256>.png
├─ README.md                 optional
├─ LICENSE.txt               optional
└─ docs/                     optional Markdown only
```

`package.rino.json` conforms to the V1 schema. Its `files` collection lists every payload,
license, readme, and documentation file, but not `package.rino.json` or
`signature.ed25519`. Every listed file has an exact byte length and lowercase SHA-256.

`signature.ed25519` is a detached Ed25519 signature over the exact UTF-8 bytes of
`package.rino.json`. The catalog identifies the approved publisher key. The client first
verifies the catalog's digest of the complete archive, then the package manifest signature,
then every manifest-listed file.

The archive may contain only JSON graph/project data, PNG project assets, Markdown
documentation, and plain-text license data. Executables, libraries, command files, Python
modules, model binaries, nested archives, symlinks, hard links, and unlisted files are
rejected.

## 5. Package identity and compatibility

`packageId` and `version` form the immutable package-version identity. User-visible names
never become directory names or configuration file names.

The manifest declares:

- Package type and stable publisher identity.
- Localized display name, summary, and optional description.
- Optional game identity and localizable game name.
- License and source GitHub Release coordinates.
- Minimum and optional maximum compatible client versions.
- Required Sidecar protocol version and accepted Rino graph schema versions.
- Required allowlisted runtime capabilities.
- Stable entry points and the default complete-project entry point where applicable.

Compatibility is checked before extraction and again before activation. A package that is
newer than the client understands is rejected before installation and is never partially
loaded.

## 6. Package types and import semantics

### 6.1 `project`

A project package supports complete-project import only. Importing it:

1. Installs the package version once in the package store.
2. Creates a new configuration with a generated stable configuration ID.
3. Binds that configuration to `packageId`, version, and the declared default entry point.
4. Initializes task choices from the package entry-point metadata.
5. Offers `Switch now` or `Switch later` after activation succeeds.

Deleting the configuration does not delete shared package bytes. Uninstall is allowed only
when no configuration references the installed package version.

### 6.2 `feature`

A feature package supports individual-feature import only. Each exported entry point is a
self-contained feature whose dependency closure is already present in the package.

Importing a feature adds a stable reference to the reserved configuration:

```text
packageId + packageVersion + entrypointId
```

The client does not merge graph JSON or image files across packages. Namespacing by package
identity prevents graph, node, entry-point, and asset collisions. Reimporting the same
identity is idempotent and is presented as an update or already-installed result.

### 6.3 `hybrid`

A hybrid package supports both modes. The user may create the complete-project
configuration or select one or more declared entry points to add to `My Scripts`. The client
does not invent additional importable entry points.

V1 packages are self-contained. Cross-package runtime dependencies are not supported.

## 7. Curated GitHub catalog

The client is configured with one approved catalog repository and retrieves the latest
stable `catalog-v1.json` Release Asset. It does not call GitHub search and does not accept a
user-supplied owner, repository, release, or download URL.

The catalog contains only presentation and resolution data needed before package download:

```json
{
  "schemaVersion": 1,
  "generatedAt": "2026-08-10T00:00:00Z",
  "expiresAt": "2026-08-11T00:00:00Z",
  "publisherKeys": [
    {
      "publisherId": "approved-publisher",
      "publisherName": "Approved Publisher",
      "keyId": "official-2026-01",
      "algorithm": "ed25519",
      "publicKeyBase64": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="
    }
  ],
  "packages": [
    {
      "packageId": "io.rino.example.game",
      "version": "1.2.3",
      "packageType": "hybrid",
      "displayName": {
        "default": "Example Game Automation"
      },
      "summary": {
        "default": "Reviewed automation for the example game."
      },
      "publisherKeyId": "official-2026-01",
      "owner": "approved-owner",
      "repository": "approved-repository",
      "releaseTag": "v1.2.3",
      "releaseAssetId": 123456789,
      "assetName": "example-1.2.3.rino-package",
      "byteLength": 1048576,
      "sha256": "0000000000000000000000000000000000000000000000000000000000000000",
      "releasedAt": "2026-08-10T00:00:00Z",
      "compatibility": {
        "minimumClientVersion": "1.0.0",
        "sidecarProtocolVersion": 1,
        "projectSchemaVersions": [1],
        "operatingSystem": "windows",
        "architectures": ["x86_64"]
      }
    }
  ],
  "signing": {
    "algorithm": "ed25519",
    "keyId": "catalog-root-2026-01",
    "signatureFile": "catalog-v1.sig"
  }
}
```

The catalog conforms to
[Rino Package Catalog V1](contracts/rino-catalog-v1.schema.json) and has a detached
signature. Package cards use signed catalog text and bundled application icons only; the
client does not load remote preview images or arbitrary remote HTML.

The client resolves downloads by the signed `releaseAssetId`; owner, repository, release
tag, asset name, reported size, and digest must still agree with the signed catalog record.
An asset replacement or metadata mismatch is a validation failure, not an implicit update.

Catalog requests use a stable GitHub REST API version, a product-specific user agent,
`ETag`/`If-None-Match`, a bounded refresh interval, and an on-disk last-known-good cache.
Public packages do not require a GitHub token. Rate-limit responses honor `Retry-After` and
`X-RateLimit-Reset`; the client does not spin or retry indefinitely.

The packaged client pins the central catalog to `SweetSmellFox/MFAWPF` and the Ed25519
root key ID `catalog-root-2026-08`. Package and catalog contents remain plaintext. Ed25519
is used only for publisher authentication and integrity verification, not encryption. The
unencrypted root private key remains in the ignored local `.ai-local/signing-keys/`
directory and is operated through `tools/RinoPackageKeyTool`; it is never a release asset.

## 8. Storage and persistence

Packages, staging files, the catalog cache, and installation state live under an
application-owned per-user data root, never beside the executable and never inside a Rino
authoring project.

```text
<application-data>/
├─ package-catalog/
│  ├─ current.json
│  ├─ catalog-<sha256>.json
│  └─ catalog-<sha256>.sig
├─ package-downloads/
│  └─ <random>.partial
├─ package-staging/
│  └─ <random>/
├─ packages/
│  └─ <opaque-package-key>/
│     └─ <opaque-version-key>/
└─ package-state/
   └─ installed-packages-v1.json
```

Package and version keys are deterministic, filesystem-safe encodings or hashes of the
immutable package identity. Raw IDs, versions, and display names are never used as path
segments.

The implemented `installed-packages-v1.json` state has schema version `1` and keeps two
bounded collections. A project binding records a generated configuration UUID, the local
configuration display name, exact package/version/entry-point identity, import timestamp,
and activation state. A feature binding records the same package reference against the fixed
`My Scripts` configuration UUID. Paths and graph content are not persisted in this state.
The only currently valid activation state is `pendingSidecar`; writing another value is
rejected rather than interpreted as active.

Before implementation creates any of these locations, its private or user-specific
patterns must be excluded from version control through `.gitignore`. Catalog data may reveal installed interests, and package state contains
machine-local lifecycle information; neither is published or logged.

Installation uses a staging directory on the same volume as the package store. The client
commits by atomic directory rename only after all checks pass. A failed installation removes
only its exact random staging directory and leaves the active version unchanged.

Versions install side by side. Updating a configuration switches its reference only after
the new version validates and the Sidecar accepts it. The previous version remains available
for rollback until no active reference or recovery record needs it.

## 9. Configuration model

Configuration display names are not identities. The new persisted model uses generated IDs:

```text
ConfigurationProfileV2
├─ configurationId
├─ displayName
├─ kind: featureCollection | project | legacy
├─ projectBinding?: PackageEntrypointReferenceV1
├─ featureBindings: PackageEntrypointReferenceV1[]
└─ existing user task choices and settings
```

`PackageEntrypointReferenceV1` contains `packageId`, exact installed version, and
`entrypointId`.

The client creates a separate reserved `My Scripts` configuration and displays it as
`My Scripts` in English or `我的脚本` in Simplified Chinese. It cannot be renamed to collide
with a project, deleted, or replaced by a downloaded package. The existing default and named
configurations remain available as legacy profiles until a later package-aware migration.

The WPF compatibility bridge currently keeps the stable bindings in the package-state file.
For a project import it also creates one ordinary configuration JSON containing only its
generated `rinoPackageConfigurationId` marker, so the existing configuration selector can
show it immediately. Package identity, version, entry point, and installed paths remain out
of that legacy configuration file. Package-created configurations are protected from the
legacy delete button until a package-aware remove operation can update both stores
atomically.

Changing the selected configuration must no longer restart the whole application. The
client saves pending choices, rejects a switch while a device-affecting run cannot be safely
cancelled, asks the Sidecar to load and validate the target package set, and commits the UI
selection only after a successful handshake. Failure restores the previous active profile.

## 10. Runtime boundary

The WPF client never interprets or executes graph nodes. A runtime session is established
through versioned structured IPC with request IDs and explicit success/error outcomes.

A configuration activation passes package and entry-point identities to a trusted package
resolver. Only that resolver converts identities into canonical paths under the package
store. The Sidecar receives the resolved read-only project root, validates the package
manifest, project manifest, graph documents, asset bindings, capability allowlist, and
entry-point identity again, then returns a load result.

The boundary explicitly handles:

- Protocol/version mismatch.
- Missing, removed, or partially installed package versions.
- Invalid manifest, graph, asset, or capability declarations.
- Cancellation and timeout.
- Sidecar exit, stale response, and reconnect.
- Active-run conflict during configuration switching or package update.

Downloaded content cannot extend the node registry, load plugins, execute commands, or add
native/Python code. Only capabilities already bundled and allowlisted by the installed client
may become available.

### 10.1 Current runtime gap and required Rino handoff

The current Rino protocol exposes `graph.validate` and `run.start`. Both accept a complete
in-memory project document, and `run.start` also requires a graph UUID. The client now uses
the existing `system.handshake` and `graph.validate` contracts for a real runtime preflight:

1. It resolves only `rino-runtime.exe` beside the application executable. There is no PATH,
   environment-variable, authoring-workspace, or packaging-spike fallback.
2. It re-verifies the installed package, then reads and hashes the project manifest and graph
   files again immediately before use.
3. It assembles the split authoring layout into the complete project document expected by
   `graph.validate`, while checking document, graph, file, and entry-point identities.
4. It starts the Sidecar without a shell and uses bounded Content-Length framing, strict JSON,
   UUID request correlation, version/frame-limit negotiation, cancellation, request timeout,
   output closure detection, and bounded process-tree cleanup.
5. A successful `graph.validate` result is displayed as a passed preflight. A non-executable
   result or transport failure leaves the import binding intact but never marks it active.

The remaining production gap is execution activation. The Rino release pipeline must produce
the approved `rino-runtime.exe` and the packaged client must include it beside the WPF
executable. The client then needs an execution session that safely resolves project image
assets into `assetBindings`, supplies the selected device session, calls `run.start`, routes
ordered events, and implements cancellation/reconnect without falling back to the legacy Maa
executor. Until that run boundary is implemented and tested cross-language, persisted imports
remain `pendingSidecar` even when their validation preflight succeeds.

## 11. Secure download and extraction

The package installer is independent from the existing application/resource updater. It may
reuse shared HTTP ownership and presentation concepts only after they are made asynchronous,
cancellable, and bounded; it must not reuse direct `ZipFile.ExtractToDirectory` installation.

V1 client policy limits are:

- 1 GiB maximum downloaded archive bytes.
- 2 GiB maximum total expanded bytes.
- 4,096 maximum archive entries.
- 4 MiB maximum package or project manifest.
- 64 graph files and 16 MiB per graph file.
- 2,000 PNG image objects and 256 MiB per image object.
- Five HTTPS redirects maximum, restricted to approved GitHub asset hosts.

The downloader stops when the actual byte count exceeds a limit even if `Content-Length` is
missing or false. Extraction rejects absolute paths, drive prefixes, alternate data streams,
`.`/`..`, separators hidden by normalization, Windows reserved device names, case-folded
collisions, symlinks, hard links, unsupported compression methods, duplicate entries, and
entries absent from the manifest.

All temporary names are random and resolved under the exact application-owned staging root.
Cleanup operates only on a verified exact child path and never follows links.

## 12. Download-center UX

The Settings shell keeps configuration switching visible at the top. The left navigation
adds `Downloads`; selecting it opens a dedicated right-side page while preserving existing
settings navigation and scroll state.

The page has three local views:

- Discover: cached approved catalog with search and project/feature filters.
- Installed: installed versions, configuration usage, source, license, and uninstall state.
- Updates: compatible stable updates with release notes and explicit user initiation.

Every operation exposes loading, progress, cancellation, retry, offline, rate-limited,
incompatible, signature-failed, disk-full, and rollback states. Status is conveyed by text
and icon as well as color. Downloading never blocks the UI thread.

After installation, a project package offers `Import as project`; a feature package offers
`Add to My Scripts`; a hybrid package offers both. The implemented client enables these
controls only after re-verifying the installed manifest signature, catalog identity,
compatibility, exact file set, file sizes, and hashes. The resulting binding remains visibly
pending. When the approved bundled runtime is present, the client immediately performs the
Section 10.1 graph preflight after import and reports its outcome; execution remains disabled
until the `run.start` session and asset/device bindings are implemented.

## 13. Rino publisher requirements

The future Rino publishing workflow must:

1. Require a saved, current-schema project with no blocking diagnostics.
2. Let the publisher choose `project`, `feature`, or `hybrid`.
3. For feature export, select one or more callable entry graphs and compute their complete
   graph and asset dependency closure.
4. Reject an entry point whose closure is incomplete, cyclic in an unsupported way, or needs
   a capability unavailable to distributable packages.
5. Copy only manifest-declared graph and PNG asset objects.
6. Create stable entry-point IDs independent of localized names.
7. Produce canonical `package.rino.json` bytes and deterministic ZIP entry ordering.
8. Generate the detached signature with an explicitly selected publisher key.
9. Reopen and fully validate the produced archive before reporting success.
10. Output the local `.rino-package` and catalog-entry draft without uploading anything.

When Rino adopts this contract, the schema should become a canonical source under its
contracts tree. Until a shared contract repository exists, the client copy is a normative
draft and both repositories must pin and verify the schema SHA-256 in their contract checks.

## 14. Delivery sequence

1. Approve this document and the V1 package schema with valid/invalid fixtures.
2. Define and test the Sidecar package-load IPC messages.
3. Add per-user package storage and migrate the reserved `My Scripts` configuration.
4. Implement validation, bounded download, safe extraction, atomic install, and rollback.
5. Implement configuration binding and Sidecar activation without application restart.
6. Add the Settings download-center UI and localization.
7. Define and sign the curated catalog contract.
8. Implement the local Rino publisher in the Rino repository under a separately authorized
   task.
9. Run cross-language fixtures and end-to-end project/feature import gates.

## 15. Required validation

The implementation is incomplete until automated coverage proves:

- Valid project, feature, and hybrid packages install and activate.
- Invalid schema, unknown fields, unsupported versions, capability violations, bad hashes,
  bad signatures, unsafe paths, case collisions, duplicate entries, oversized streams,
  truncated downloads, disk-full writes, cancellation, and Sidecar failure leave no active
  partial installation.
- Complete-project import creates one stable configuration and can switch without restarting
  the client.
- Feature imports coexist in `My Scripts` without graph or asset collisions and are
  idempotent.
- Update activation is atomic and rollback preserves the previous working version.
- Package uninstall refuses referenced versions and removes only the exact selected version.
- Offline catalog cache and rate-limit recovery behave deterministically.
- Neither client logs nor IPC payloads expose tokens, private paths, device identifiers,
  project image bytes, or user configuration content.

### 15.1 Semantic validation beyond JSON Schema

Passing the JSON Schema is necessary but not sufficient. Before extraction or activation,
the package validator also rejects a manifest unless all of the following are true:

- Normalized file paths are unique under Windows ordinal-ignore-case comparison, contain no
  reserved device name, and identify every non-signature archive entry exactly once.
- There is exactly one project manifest, between one and 64 graph files, and no more than
  2,000 image files.
- Entrypoint IDs are unique. Every entrypoint references a graph declared by the embedded
  project manifest, and `defaultProjectEntrypointId` references one of those declared
  entrypoints for complete-project activation.
- Graph IDs, filenames, and project-manifest graph records agree; all graph and image
  references stay inside the package payload.
- Each content-addressed image filename equals the lowercase SHA-256 of its exact bytes.
- Every declared file size and SHA-256 matches the streamed archive content before the file
  is accepted into staging.
- A declared `LICENSE.txt`, `README.md`, or documentation file has a matching file record,
  and no file record aliases another archive entry.
- GitHub owner, repository, tag, asset name, package identity, and release version agree with
  the signed catalog record selected by the user.

These checks run against bounded streams before activation and are repeated when an installed
version is opened after an application upgrade that changes the validator contract.

## 16. References

- Rino authoring repository: `docs/architecture/adr/0006-project-directory-authoring-format.md`,
  `contracts/graph/rino-graph-v1.schema.json`, and `contracts/ipc/rino-ipc-v1.schema.json`.
- GitHub REST API: [Releases](https://docs.github.com/en/rest/releases/releases),
  [Release assets](https://docs.github.com/en/rest/releases/assets), and
  [rate limits](https://docs.github.com/en/rest/using-the-rest-api/rate-limits-for-the-rest-api).
- GitHub REST guidance: [Best practices](https://docs.github.com/en/rest/using-the-rest-api/best-practices-for-using-the-rest-api).
- JSON Schema: [Draft 2020-12](https://json-schema.org/draft/2020-12).
