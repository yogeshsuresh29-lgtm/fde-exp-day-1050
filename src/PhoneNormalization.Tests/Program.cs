using BankingApp.Tools;

// This is what "find the one failing case" actually means concretely —
// run this, read which customer fails and why, fix
// ../BankingApp/Tools/PhoneNormalizer.cs, re-run until it's 9/9.
// Matches ci.yml's phone-normalization step.
//
// Notes on this suite versus the orginial new-pack version:
//   * The linked normalizer (BankingApp.Tools.PhoneNormalizer) is the
//     consolidated app's INSTANCE API, not a static Normalize(string?),
//     so this suite drops the null-input case — a 10-digit US number is
//     the calling contract (the app never passes null).
//   * The deliberate bug survives: customer 10's "ext 4" makes the digit
//     count 11 without a leading 1, which the starter implementation
//     rejects instead of ignoring the extension.

var cases = new (int CustomerId, string Raw, string Expected)[]
{
    (1,  "(555) 123-4567",     "+15551234567"),
    (2,  "555-987-6543",       "+15559876543"),
    (3,  "5551234432",         "+15551234432"),
    (4,  "+1 555 222 3344",    "+15552223344"),
    (5,  "555.876.1122",       "+15558761122"),
    (6,  "  555-444-9988  ",   "+15554449988"),
    (7,  "(555)6667777",       "+15556667777"),
    (8,  "15553219876",        "+15553219876"),
    (10, "555 123 0001 ext 4", "+15551230001"),
};

int passed = 0, failed = 0;
foreach (var (id, raw, expected) in cases)
{
    var actual = new PhoneNormalizer().NormalizePhone(raw);
    var ok = actual == expected;
    if (ok) passed++; else failed++;
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] customer {id,2}  raw='{raw}'  expected='{expected}'  actual='{actual}'");
}

Console.WriteLine($"\n{passed}/{cases.Length} passed, {failed} failed");
Environment.Exit(failed == 0 ? 0 : 1);