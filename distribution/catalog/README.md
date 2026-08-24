# Rino Package Catalog Release Assets

Upload `catalog-v1.json` and `catalog-v1.sig` as assets of the latest stable GitHub
Release in `SweetSmellFox/MFAWPF`. The packaged client resolves both assets by their
exact names and rejects draft or prerelease catalogs.

The checked-in catalog is public release data. The unencrypted root private key is local
only under `.ai-local/signing-keys/` and must never be committed or uploaded.

After editing the catalog source, replace the signature with:

```powershell
dotnet run --project tools\RinoPackageKeyTool\RinoPackageKeyTool.csproj -- sign `
  --private-key .ai-local\signing-keys\catalog-root-2026-08.private.json `
  --input distribution\catalog\catalog-v1.json `
  --signature distribution\catalog\catalog-v1.sig
```

The signing tool refuses to overwrite an existing signature. Remove the old signature
only after reviewing the catalog diff and immediately generate its replacement.
