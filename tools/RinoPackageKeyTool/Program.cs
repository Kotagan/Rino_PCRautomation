using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RinoPackageKeyTool;

internal static class Program
{
    private const int MaximumCatalogBytes = 4 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                WriteUsage();
                return 2;
            }

            return args[0] switch
            {
                "generate" => await GenerateAsync(ParseOptions(args[1..])),
                "sign" => await SignAsync(ParseOptions(args[1..])),
                _ => throw new ArgumentException("Unknown command."),
            };
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            IOException or
            JsonException or
            UnauthorizedAccessException)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static async Task<int> GenerateAsync(IReadOnlyDictionary<string, string> options)
    {
        var keyId = GetRequiredOption(options, "key-id");
        ValidateKeyId(keyId);
        var privateKeyPath = ResolveNewFilePath(GetRequiredOption(options, "private-key"));
        var publicKeyPath = ResolveNewFilePath(GetRequiredOption(options, "public-key"));
        if (privateKeyPath.Equals(publicKeyPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Private and public key paths must differ.");
        }

        var privateKey = new Ed25519PrivateKeyParameters(new SecureRandom());
        var keyFile = new SigningKeyFile
        {
            FormatVersion = 1,
            Algorithm = "ed25519",
            KeyId = keyId,
            PrivateKeyBase64 = Convert.ToBase64String(privateKey.GetEncoded()),
            PublicKeyBase64 = Convert.ToBase64String(privateKey.GeneratePublicKey().GetEncoded()),
        };
        var publicKeyFile = new SigningPublicKeyFile
        {
            FormatVersion = 1,
            Algorithm = "ed25519",
            KeyId = keyId,
            PublicKeyBase64 = keyFile.PublicKeyBase64,
        };

        await WriteNewJsonAsync(privateKeyPath, keyFile);
        try
        {
            await WriteNewJsonAsync(publicKeyPath, publicKeyFile);
        }
        catch
        {
            File.Delete(privateKeyPath);
            throw;
        }

        Console.WriteLine($"Generated Ed25519 signing key '{keyId}'.");
        Console.WriteLine($"Private key: {privateKeyPath}");
        Console.WriteLine($"Public key: {publicKeyPath}");
        return 0;
    }

    private static async Task<int> SignAsync(IReadOnlyDictionary<string, string> options)
    {
        var privateKeyPath = ResolveExistingFilePath(GetRequiredOption(options, "private-key"));
        var inputPath = ResolveExistingFilePath(GetRequiredOption(options, "input"));
        var signaturePath = ResolveNewFilePath(GetRequiredOption(options, "signature"));
        var input = await ReadBoundedFileAsync(inputPath, MaximumCatalogBytes);
        var keyBytes = await ReadBoundedFileAsync(privateKeyPath, 16 * 1024);
        var keyFile = JsonSerializer.Deserialize<SigningKeyFile>(keyBytes, JsonOptions)
            ?? throw new JsonException("Signing key file is empty.");
        ValidateSigningKey(keyFile);

        var privateKey = new Ed25519PrivateKeyParameters(
            Convert.FromBase64String(keyFile.PrivateKeyBase64),
            0);
        var signer = new Ed25519Signer();
        signer.Init(true, privateKey);
        signer.BlockUpdate(input, 0, input.Length);
        await WriteNewBytesAsync(signaturePath, signer.GenerateSignature());

        Console.WriteLine($"Signed catalog with key '{keyFile.KeyId}'.");
        Console.WriteLine($"Signature: {signaturePath}");
        return 0;
    }

    private static Dictionary<string, string> ParseOptions(IReadOnlyList<string> args)
    {
        if (args.Count == 0 || args.Count % 2 != 0)
        {
            throw new ArgumentException("Options must use '--name value' pairs.");
        }

        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Count; index += 2)
        {
            var name = args[index];
            if (!name.StartsWith("--", StringComparison.Ordinal)
                || name.Length <= 2
                || !options.TryAdd(name[2..], args[index + 1]))
            {
                throw new ArgumentException("Command options are invalid or duplicated.");
            }
        }

        return options;
    }

    private static string GetRequiredOption(
        IReadOnlyDictionary<string, string> options,
        string name) =>
        options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"Missing required option '--{name}'.");

    private static void ValidateSigningKey(SigningKeyFile keyFile)
    {
        ValidateKeyId(keyFile.KeyId);
        if (keyFile.FormatVersion != 1 || keyFile.Algorithm != "ed25519")
        {
            throw new JsonException("Signing key file format is unsupported.");
        }

        byte[] privateKey;
        byte[] publicKey;
        try
        {
            privateKey = Convert.FromBase64String(keyFile.PrivateKeyBase64);
            publicKey = Convert.FromBase64String(keyFile.PublicKeyBase64);
        }
        catch (FormatException exception)
        {
            throw new JsonException("Signing key contains invalid Base64.", exception);
        }

        if (privateKey.Length != 32 || publicKey.Length != 32)
        {
            throw new JsonException("Signing key has an invalid Ed25519 key length.");
        }

        var derivedPublicKey = new Ed25519PrivateKeyParameters(privateKey, 0)
            .GeneratePublicKey()
            .GetEncoded();
        if (!derivedPublicKey.AsSpan().SequenceEqual(publicKey))
        {
            throw new JsonException("Signing key public and private components do not match.");
        }
    }

    private static void ValidateKeyId(string keyId)
    {
        if (string.IsNullOrWhiteSpace(keyId)
            || keyId.Length > 128
            || keyId.Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not '.' and not '_' and not ':' and not '-'))
        {
            throw new ArgumentException("Signing key ID is invalid.");
        }
    }

    private static string ResolveNewFilePath(string path)
    {
        var resolvedPath = Path.GetFullPath(path);
        if (File.Exists(resolvedPath) || Directory.Exists(resolvedPath))
        {
            throw new IOException($"Refusing to overwrite existing path: {resolvedPath}");
        }

        var parent = Path.GetDirectoryName(resolvedPath)
            ?? throw new ArgumentException("Output path has no parent directory.");
        Directory.CreateDirectory(parent);
        if ((File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("Output directory cannot be a reparse point.");
        }

        return resolvedPath;
    }

    private static string ResolveExistingFilePath(string path)
    {
        var resolvedPath = Path.GetFullPath(path);
        var file = new FileInfo(resolvedPath);
        if (!file.Exists || (file.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new FileNotFoundException("Input file is unavailable or unsafe.", resolvedPath);
        }

        return resolvedPath;
    }

    private static async Task<byte[]> ReadBoundedFileAsync(string path, int maximumBytes)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length is <= 0 || stream.Length > maximumBytes)
        {
            throw new IOException("Input file size is invalid.");
        }

        var bytes = new byte[stream.Length];
        await stream.ReadExactlyAsync(bytes);
        return bytes;
    }

    private static Task WriteNewJsonAsync<T>(string path, T value) =>
        WriteNewBytesAsync(path, JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions));

    private static async Task WriteNewBytesAsync(string path, byte[] bytes)
    {
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
    }

    private static void WriteUsage()
    {
        Console.Error.WriteLine(
            "Usage:\n" +
            "  generate --key-id <id> --private-key <path> --public-key <path>\n" +
            "  sign --private-key <path> --input <catalog.json> --signature <catalog.sig>");
    }

    private sealed class SigningKeyFile
    {
        public int FormatVersion { get; init; }

        public string Algorithm { get; init; } = string.Empty;

        public string KeyId { get; init; } = string.Empty;

        public string PrivateKeyBase64 { get; init; } = string.Empty;

        public string PublicKeyBase64 { get; init; } = string.Empty;
    }

    private sealed class SigningPublicKeyFile
    {
        public int FormatVersion { get; init; }

        public string Algorithm { get; init; } = string.Empty;

        public string KeyId { get; init; } = string.Empty;

        public string PublicKeyBase64 { get; init; } = string.Empty;
    }
}
