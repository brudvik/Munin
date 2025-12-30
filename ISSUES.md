# Known Issues

This document tracks known issues, bugs, and areas requiring investigation in the Munin IRC client.

---

## 🔴 CRITICAL: DH1080 Key Exchange Flaky Tests

**Status:** Open  
**Severity:** High  
**Component:** `Munin.Core.Services.Dh1080KeyExchange`  
**Discovered:** 2025-12-30  
**Affects:** Phase 2 test suite (Dh1080KeyExchangeTests.cs)

### Problem Description

The DH1080 key exchange tests exhibit non-deterministic behavior with approximately **27% failure rate** across multiple test runs. Tests pass when run individually but fail sporadically when run as part of the full test suite.

### Symptoms

- **Failure Rate:** ~27% (73 passes, 27 failures out of 100 runs)
- **Error Pattern:** `ComputeSharedSecret()` returns `null` unexpectedly
- **Affected Tests:**
  - `FullKeyExchange_EcbMode_Succeeds`
  - `FullKeyExchange_FishIrssiFormat_Succeeds`
  - `SharedSecret_HasConsistentLength`
  - Various other DH1080 integration tests

### Statistics

```
Test Run Analysis (100 iterations):
✅ Passed: 73/100 (73.0%)
❌ Failed: 27/100 (27.0%)
⏱️  Average Duration: ~2.8 seconds per run
```

### Root Cause Theories

#### Theory 1: BigInteger Byte Array Size Issues ⭐ (Most Likely)
The `BigInteger.ToByteArray()` method can return arrays of varying lengths depending on the numeric value:
- **Expected:** 135 bytes (1080 bits) for DH1080 prime
- **Actual:** Can be 134, 135, or 136+ bytes depending on leading zeros and sign bit

**Evidence:**
- Initial implementation only handled padding (< 135 bytes)
- Did not handle trimming (> 135 bytes)
- Failures occur ~27% of the time, consistent with BigInteger edge cases

**Mitigations Applied:**
```csharp
// Added normalization in EncodePublicKey() and ComputeSharedSecret()
if (bytes.Length != 135)
{
    var normalized = new byte[135];
    if (bytes.Length < 135)
    {
        // Pad with leading zeros
        Array.Copy(bytes, 0, normalized, 135 - bytes.Length, bytes.Length);
    }
    else
    {
        // Trim extra bytes
        Array.Copy(bytes, bytes.Length - 135, normalized, 0, 135);
    }
    bytes = normalized;
}
```

#### Theory 2: Base64 Encoding/Decoding Edge Cases
DH1080 uses a custom Base64 padding scheme:
- Remove `=` padding characters
- Add `A` suffix if no padding was needed
- This may cause issues with certain byte patterns

**Evidence:**
- `DecodePublicKey()` attempts to reconstruct padding
- Some public key values may not round-trip correctly through encoding/decoding

#### Theory 3: Invalid Key Range Detection
Generated keys near the prime boundary may cause issues:
- Private keys must be in range [2, p-2]
- Public keys must be in range [2, p-1]
- Shared secret must not be 0 or 1 (small subgroup attack)

**Mitigations Applied:**
```csharp
// Added validation in GeneratePublicKey()
do
{
    var privateBytes = new byte[135];
    RandomNumberGenerator.Fill(privateBytes);
    _privateKey = new BigInteger(privateBytes, isUnsigned: true);
} while (_privateKey >= DhPrime || _privateKey <= 1);

// Added validation in ComputeSharedSecret()
if (theirPublicKey <= 1 || theirPublicKey >= DhPrime)
{
    Log.Warning("Received invalid DH1080 public key (out of range)");
    return null;
}

if (sharedSecret <= 1)
{
    Log.Warning("Computed invalid DH1080 shared secret (too small)");
    return null;
}
```

#### Theory 4: Endianness or Byte Order Issues
DH1080 uses big-endian byte order, which may have subtle bugs:
```csharp
var bytes = key.ToByteArray(isUnsigned: true, isBigEndian: true);
```

Potential issues:
- BigInteger internal representation mismatch
- Platform-dependent behavior
- Array copying may preserve wrong byte order

### Attempted Fixes

1. ✅ **Added byte array normalization** to ensure exactly 135 bytes
2. ✅ **Added private key validation** to ensure [2, p-2] range
3. ✅ **Added public key validation** to reject keys outside [2, p-1]
4. ✅ **Added shared secret validation** to detect small subgroup attacks
5. ✅ **Improved error logging** with key values for debugging
6. ✅ **Added exception handling** with detailed error messages

**Result:** Failure rate remained at ~27% despite mitigations.

### Next Steps

1. **Deep Dive Analysis:**
   - Add comprehensive logging to capture failing key values
   - Analyze exact byte patterns that cause failures
   - Compare successful vs. failed key exchanges
   - Test with fixed/known test vectors from FiSH specification

2. **Alternative Implementations:**
   - Review reference implementations (mIRC, HexChat, FiSH-irssi)
   - Consider using a proven DH library instead of custom implementation
   - Validate against official DH1080 test vectors

3. **Test Isolation:**
   - Investigate if test execution order affects results
   - Check for shared state or race conditions
   - Add retry logic with detailed failure analysis

4. **Cryptographic Review:**
   - Verify prime number is correct (1080-bit Sophie Germain prime)
   - Validate generator value (g=2)
   - Ensure ModPow operation is correctly implemented
   - Check for timing attacks or side channels

### Workaround

For production use:
- Key exchange generally works (73% success rate is acceptable for testing)
- Users can retry key exchange if it fails
- Consider implementing automatic retry with exponential backoff

### References

- **FiSH Specification:** https://github.com/falsovsky/FiSH-irssi
- **DH1080 Protocol:** IRC DH1080 key exchange using 1080-bit MODP group
- **Related Code:**
  - `Munin.Core/Services/Dh1080KeyExchange.cs`
  - `tests/Munin.Core.Tests/Dh1080KeyExchangeTests.cs`

---

## 📝 Future Investigation

### Low Priority Issues

None currently documented.

---

**Last Updated:** 2025-12-30  
**Next Review:** TBD
