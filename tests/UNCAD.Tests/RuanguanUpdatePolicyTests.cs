using UNCAD.Features.Fill;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class RuanguanUpdatePolicyTests
    {
        [Fact]
        public void ExistingBlockWithoutLength_IsInvalid()
            => Assert.Equal(RuanguanLengthState.InvalidOrUnknown,
                FillFeature.ClassifyRuanguanLength(1, true, true, ""));

        [Fact]
        public void IncompleteUpdateSelection_CannotConfirmBlockDeletion()
            => Assert.Equal(RuanguanLengthState.InvalidOrUnknown,
                FillFeature.ClassifyRuanguanLength(0, false, true, ""));

        [Fact]
        public void CompleteUpdateSelectionWithoutBlock_ConfirmsDeletion()
            => Assert.Equal(RuanguanLengthState.ConfirmedAbsent,
                FillFeature.ClassifyRuanguanLength(0, true, true, ""));

        [Fact]
        public void ParsedLength_IsValidRegardlessOfSelectionScope()
            => Assert.Equal(RuanguanLengthState.Valid,
                FillFeature.ClassifyRuanguanLength(1, false, true, "1.5"));
    }
}
