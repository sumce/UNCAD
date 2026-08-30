namespace UNCAD.Infra
{
    public static class Branding
    {
        public const string Nameplate = ProductMetadata.ProductName + " · " + ProductMetadata.ProductSubtitle;
        public static string Edition => ProductMetadata.CurrentLicense().StatusText;
        public const string ReleaseUpdatedOn = ProductMetadata.ReleaseDateUtc;
        public const string Developer = ProductMetadata.CompanyName;
        public const string Website = ProductMetadata.Website;
        public const string WebsiteUrl = ProductMetadata.WebsiteUrl;
    }
}
