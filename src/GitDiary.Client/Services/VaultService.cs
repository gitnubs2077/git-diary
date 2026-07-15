using System.Text.Json;
using GitDiary.Client.Models;
using Microsoft.JSInterop;

namespace GitDiary.Client.Services;

/// <summary>
/// The encrypted-at-rest boundary for everything sensitive this app keeps in the
/// browser: the <see cref="RepositoryConfig"/> (which carries the GitHub PAT) and the
/// draft cache (which carries diary text).
/// </summary>
/// <remarks>
/// <para>
/// When a vault exists, the config lives ONLY as ciphertext under
/// <c>gitdiary_vault</c> — the old plaintext <c>gitdiary_owner/repo/branch/token</c>
/// keys are removed. The actual AES-GCM key is derived from the user's password by
/// Web Crypto and held there as a non-extractable CryptoKey; this class never sees
/// the raw key, only asks JS to encrypt/decrypt with it.
/// </para>
/// <para>
/// <see cref="IsUnlocked"/> mirrors the JS-side key state. It is process-lifetime
/// only: a reload starts locked and the lock screen must re-derive the key. This
/// service is a singleton so that state is shared with <see cref="IndexedDbRepository"/>,
/// which consults it to encrypt/decrypt drafts.
/// </para>
/// </remarks>
public sealed class VaultService
{
    private readonly IJSRuntime _js;

    // Config vault.
    private const string VaultKey = "gitdiary_vault";
    // Legacy plaintext config keys — removed when a vault is created, restored when it
    // is removed. Kept in one place so the two flows can't drift.
    private static readonly string[] PlaintextConfigKeys =
        { "gitdiary_owner", "gitdiary_repo", "gitdiary_branch", "gitdiary_token" };

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public VaultService(IJSRuntime js) => _js = js;

    /// <summary>True once the derived key is cached (this session). Reset by a reload.</summary>
    public bool IsUnlocked { get; private set; }

    /// <summary>Whether a password-protected vault exists in this browser.</summary>
    public async Task<bool> HasVaultAsync()
        => !string.IsNullOrEmpty(await GetItemAsync(VaultKey));

    /// <summary>
    /// Encrypt <paramref name="config"/> under a brand-new password and persist it as
    /// the vault, deleting the plaintext config keys. Also migrates any existing
    /// plaintext draft cache to ciphertext under the new key.
    /// </summary>
    public async Task CreateAsync(string password, RepositoryConfig config)
    {
        var meta = await _js.InvokeAsync<DeriveResult>("gitdiaryVault.deriveNew", password);
        IsUnlocked = true; // key is now cached JS-side

        await WriteConfigVaultAsync(config, meta);
        await MigratePlaintextDraftsToVaultAsync();

        foreach (var key in PlaintextConfigKeys)
            await RemoveItemAsync(key);
    }

    /// <summary>
    /// Try to unlock with a password. Returns the decrypted config on success, or
    /// null on the wrong password (leaving the vault locked).
    /// </summary>
    public async Task<RepositoryConfig?> UnlockAsync(string password)
    {
        var envelope = await ReadEnvelopeAsync(VaultKey);
        if (envelope is null) return null;

        await _js.InvokeAsync<bool>("gitdiaryVault.deriveExisting", password, envelope.Salt, envelope.Iterations);
        var plaintext = await _js.InvokeAsync<string?>("gitdiaryVault.decrypt", envelope.Iv, envelope.Ct);
        if (plaintext is null)
        {
            // Wrong password: the derived key doesn't match, so drop it.
            await _js.InvokeVoidAsync("gitdiaryVault.lock");
            IsUnlocked = false;
            return null;
        }

        IsUnlocked = true;
        return JsonSerializer.Deserialize<RepositoryConfig>(plaintext, Json);
    }

    /// <summary>
    /// Re-key the vault under a new password (must already be unlocked). Re-encrypts
    /// both the config and the draft cache so nothing is left under the old key.
    /// </summary>
    public async Task ChangePasswordAsync(string newPassword, RepositoryConfig config)
    {
        // Decrypt drafts with the CURRENT key first, before deriveNew swaps it out.
        var draftsPlaintext = await DecryptDraftsForRekeyAsync();

        var meta = await _js.InvokeAsync<DeriveResult>("gitdiaryVault.deriveNew", newPassword);
        IsUnlocked = true;

        await WriteConfigVaultAsync(config, meta);

        if (draftsPlaintext is not null)
        {
            var reEncrypted = await _js.InvokeAsync<EncResult>("gitdiaryVault.encrypt", draftsPlaintext);
            await SetItemAsync(DraftsKey, SerializeEnvelope(reEncrypted));
        }
    }

    /// <summary>
    /// Remove password protection: write the config back as plaintext keys, decrypt
    /// the draft cache back to plaintext, delete the vault, and lock the key.
    /// </summary>
    public async Task RemoveAsync(RepositoryConfig config)
    {
        var draftsPlaintext = await DecryptDraftsForRekeyAsync();

        await SetItemAsync("gitdiary_owner", config.Owner);
        await SetItemAsync("gitdiary_repo", config.Repo);
        await SetItemAsync("gitdiary_branch", config.Branch);
        await SetItemAsync("gitdiary_token", config.Token);

        if (draftsPlaintext is not null)
            await SetItemAsync(DraftsKey, draftsPlaintext);

        await RemoveItemAsync(VaultKey);
        await _js.InvokeVoidAsync("gitdiaryVault.lock");
        IsUnlocked = false;
    }

    /// <summary>
    /// Re-encrypt the config under the CURRENT key (no password change) — used when
    /// the user edits repo settings while unlocked. Preserves the existing salt and
    /// iterations so the same password still unlocks it. No-op if there's no vault.
    /// </summary>
    public async Task ReEncryptConfigAsync(RepositoryConfig config)
    {
        var envelope = await ReadEnvelopeAsync(VaultKey);
        if (envelope is null || !IsUnlocked) return;
        await WriteConfigVaultAsync(config, new DeriveResult { Salt = envelope.Salt!, Iterations = envelope.Iterations });
    }

    /// <summary>
    /// Lock the app: drop the in-memory key but KEEP the vault, so the same password
    /// re-opens it. Distinct from <see cref="DestroyAsync"/> (disconnect), which
    /// deletes the vault outright.
    /// </summary>
    public async Task LockAsync()
    {
        await _js.InvokeVoidAsync("gitdiaryVault.lock");
        IsUnlocked = false;
    }

    /// <summary>Drop the in-memory key and the vault entirely (used on disconnect).</summary>
    public async Task DestroyAsync()
    {
        await RemoveItemAsync(VaultKey);
        await _js.InvokeVoidAsync("gitdiaryVault.lock");
        IsUnlocked = false;
    }

    // ----- Draft-cache encryption, used by IndexedDbRepository -----

    private const string DraftsKey = "gitdiary_drafts";

    /// <summary>Wrap a plaintext drafts blob as a vault envelope string. Requires unlock.</summary>
    public async Task<string> EncryptStringAsync(string plaintext)
    {
        var enc = await _js.InvokeAsync<EncResult>("gitdiaryVault.encrypt", plaintext);
        return SerializeEnvelope(enc);
    }

    /// <summary>
    /// Unwrap a vault-enveloped drafts blob. Returns null if it isn't an envelope or
    /// can't be decrypted (treated as "no drafts" rather than a hard failure).
    /// </summary>
    public async Task<string?> DecryptStringAsync(string stored)
    {
        var env = TryParseEnvelope(stored);
        if (env is null) return null;
        return await _js.InvokeAsync<string?>("gitdiaryVault.decrypt", env.Iv, env.Ct);
    }

    // ----- internals -----

    private async Task WriteConfigVaultAsync(RepositoryConfig config, DeriveResult meta)
    {
        var plaintext = JsonSerializer.Serialize(config, Json);
        var enc = await _js.InvokeAsync<EncResult>("gitdiaryVault.encrypt", plaintext);
        var envelope = new VaultEnvelope
        {
            V = 1,
            Salt = meta.Salt,
            Iterations = meta.Iterations,
            Iv = enc.Iv,
            Ct = enc.Ct
        };
        await SetItemAsync(VaultKey, JsonSerializer.Serialize(envelope, Json));
    }

    // On create: if a plaintext drafts blob exists, re-store it encrypted under the
    // freshly-cached key so cached diary text isn't left readable.
    private async Task MigratePlaintextDraftsToVaultAsync()
    {
        var raw = await GetItemAsync(DraftsKey);
        if (string.IsNullOrEmpty(raw) || TryParseEnvelope(raw) is not null) return;
        var enc = await _js.InvokeAsync<EncResult>("gitdiaryVault.encrypt", raw);
        await SetItemAsync(DraftsKey, SerializeEnvelope(enc));
    }

    // Decrypt the drafts blob with the CURRENT key (before a re-key or removal). Null
    // when there are no drafts or the blob isn't (or can't be) decrypted.
    private async Task<string?> DecryptDraftsForRekeyAsync()
    {
        var raw = await GetItemAsync(DraftsKey);
        if (string.IsNullOrEmpty(raw)) return null;
        var env = TryParseEnvelope(raw);
        if (env is null) return raw; // already plaintext (shouldn't happen while unlocked)
        return await _js.InvokeAsync<string?>("gitdiaryVault.decrypt", env.Iv, env.Ct);
    }

    private string SerializeEnvelope(EncResult enc)
        => JsonSerializer.Serialize(new VaultEnvelope { V = 1, Iv = enc.Iv, Ct = enc.Ct }, Json);

    private static VaultEnvelope? TryParseEnvelope(string stored)
    {
        // Drafts written before a vault existed are a bare JSON object/array with no
        // "ct" field. Only treat a well-formed envelope as ciphertext.
        try
        {
            var env = JsonSerializer.Deserialize<VaultEnvelope>(stored, Json);
            return env is { Ct: not null, Iv: not null } && !string.IsNullOrEmpty(env.Ct)
                ? env
                : null;
        }
        catch
        {
            return null;
        }
    }

    private async Task<VaultEnvelope?> ReadEnvelopeAsync(string key)
    {
        var raw = await GetItemAsync(key);
        return string.IsNullOrEmpty(raw) ? null : TryParseEnvelope(raw);
    }

    private ValueTask<string?> GetItemAsync(string key) => _js.InvokeAsync<string?>("localStorage.getItem", key);
    private ValueTask SetItemAsync(string key, string value) => _js.InvokeVoidAsync("localStorage.setItem", key, value);
    private ValueTask RemoveItemAsync(string key) => _js.InvokeVoidAsync("localStorage.removeItem", key);

    private sealed class VaultEnvelope
    {
        public int V { get; set; }
        public string? Salt { get; set; }        // present only on the config vault
        public int Iterations { get; set; }      // present only on the config vault
        public string? Iv { get; set; }
        public string? Ct { get; set; }
    }

    // Shapes returned by the JS interop (camelCase on the wire).
    private sealed class DeriveResult
    {
        public string Salt { get; set; } = "";
        public int Iterations { get; set; }
    }

    private sealed class EncResult
    {
        public string Iv { get; set; } = "";
        public string Ct { get; set; } = "";
    }
}
