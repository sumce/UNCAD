using System;
using System.IO;
using Xunit;

namespace UNCAD.Tests
{
    public class BridgeLabelMigrationContractTests
    {
        [Fact]
        public void MigrationWriterReplacesLegacyDbTextWithBoqMText()
        {
            string source = File.ReadAllText(RepoFile("src", "UNCAD", "Features",
                "Fill", "BridgeLabelMigrationWriter.cs"));

            Assert.Contains("AnnotationLabelPair.TryUpgradeBridgeLabel", source);
            Assert.Contains("var replacement = new MText()", source);
            Assert.Contains("source.Erase()", source);
        }

        [Fact]
        public void U1FAndSingleU1U_MigrateInsideFinalCadTransaction()
        {
            string source = File.ReadAllText(RepoFile("src", "UNCAD", "Features",
                "Fill", "FillFeature.cs"));
            int transaction = source.IndexOf(
                "using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())",
                StringComparison.Ordinal);
            int migration = source.IndexOf(
                "BridgeLabelMigrationWriter.Migrate(transaction,",
                transaction, StringComparison.Ordinal);
            int boq = source.IndexOf(
                "AutomaticSubmissionService.Write(ctx, transaction,",
                transaction, StringComparison.Ordinal);
            int commit = source.IndexOf("transaction.Commit()", transaction,
                StringComparison.Ordinal);

            Assert.True(transaction >= 0);
            Assert.True(migration > transaction);
            Assert.True(boq > migration);
            Assert.True(commit > boq);
        }

        [Fact]
        public void BatchU1U_MigratesEveryFrameInsideSharedTransaction()
        {
            string source = File.ReadAllText(RepoFile("src", "UNCAD", "Features",
                "Fill", "BatchFillUpdateCoordinator.cs"));
            int transaction = source.IndexOf(
                "using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())",
                StringComparison.Ordinal);
            int loop = source.IndexOf("foreach (Plan plan in plans)", transaction,
                StringComparison.Ordinal);
            int migration = source.IndexOf(
                "BridgeLabelMigrationWriter.Migrate(", loop,
                StringComparison.Ordinal);
            int boq = source.IndexOf(
                "AutomaticSubmissionService.Write(ctx, transaction,",
                migration, StringComparison.Ordinal);
            int commit = source.IndexOf("transaction.Commit()", boq,
                StringComparison.Ordinal);

            Assert.True(transaction >= 0);
            Assert.True(loop > transaction);
            Assert.True(migration > loop);
            Assert.True(boq > migration);
            Assert.True(commit > boq);
        }

        private static string RepoFile(params string[] parts)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            return Path.Combine(root, Path.Combine(parts));
        }
    }
}
