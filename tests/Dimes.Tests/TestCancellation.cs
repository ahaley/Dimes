namespace Dimes.Tests;

/// <summary>The ambient per-test cancellation token, under a short name.
///
/// xUnit1051 asks that <c>TestContext.Current.CancellationToken</c> be threaded into every call that
/// accepts one, so a cancelled or timed-out run stops promptly instead of running to completion. That is
/// worth having — but the expression occurs ~640 times here, and spelled out in full it pushed hundreds
/// of call sites past 250 characters, burying the arguments that actually carry meaning (the longest line
/// reached 296 characters, against 178 before).
///
/// The analyzer is satisfied by any expression in that position, not the literal one, so this alias keeps
/// the cancellation behaviour and the readability. <c>Ct</c> deliberately matches the <c>ct</c> parameter
/// name used by every service method it is passed to.
///
/// It is a global <c>using static</c> (see the <c>Using</c> item in Dimes.Tests.csproj), so no test file
/// imports it.</summary>
internal static class TestCancellation
{
    internal static CancellationToken Ct => TestContext.Current.CancellationToken;
}
