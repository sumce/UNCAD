using System;
using System.Collections.Generic;
using System.Linq;

namespace UNCAD.Core.Fill
{
    public sealed class FillAnomaly
    {
        public FillAnomaly(string code, TableFillCategory category,
            string subject, string message)
        {
            Code = (code ?? "").Trim();
            Category = category;
            Subject = (subject ?? "").Trim();
            Message = (message ?? "").Trim();
        }

        public string Code { get; }
        public TableFillCategory Category { get; }
        public string Subject { get; }
        public string Message { get; }
    }

    /// <summary>
    /// Central anomaly policy for generated BOQ rows. Keeping this policy out of WinForms
    /// ensures preview, final confirmation, tests, and future non-UI callers classify the
    /// same missing-catalog conditions in exactly the same way.
    /// </summary>
    public static class FillAnomalyDetector
    {
        public const string MissingCatalogCode = "BOQ-CATALOG-MISSING";

        public static List<FillAnomaly> DetectCatalog(FillReviewData review)
        {
            if (review == null) throw new ArgumentNullException(nameof(review));
            return review.Items.Where(item => item.RequiresCatalogConfirmation)
                .Select(item => new FillAnomaly(MissingCatalogCode, item.Category,
                    Subject(review, item), "固定清单找不到对应的"
                        + FillReviewData.CategoryName(item.Category) + "型号"))
                .ToList();
        }

        public static FillAnomaly MissingCable(FillReviewData review)
            => DetectCatalog(review).FirstOrDefault(anomaly =>
                anomaly.Category == TableFillCategory.Cable);

        private static string Subject(FillReviewData review, FillReviewItem item)
        {
            if (item.Category == TableFillCategory.Cable)
                return string.IsNullOrWhiteSpace(review.BoqCableModel)
                    ? "电缆型号为空" : review.BoqCableModel;
            string value = (item.Name ?? "").Trim();
            return value.Length > 0 ? value : (item.Description ?? "").Trim();
        }
    }
}
