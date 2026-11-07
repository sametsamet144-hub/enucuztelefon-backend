namespace EnUcuzTelefon.Domain.Enums;

public enum StoreType
{
    Marketplace = 1,
    Retailer = 2,
    BrandOfficial = 3,
    Telecom = 4
}

public enum StoreStatus
{
    Active = 1,
    Syncing = 2,
    Error = 3,
    Paused = 4
}

public enum IntegrationMethod
{
    RestApiV2 = 1,
    XmlFeed = 2,
    MerchantCsv = 3,
    DirectPartnerApi = 4
}

public enum MatchType
{
    AutoHighConfidence = 1,
    ManualVerified = 2,
    ReviewRequired = 3,
    Rejected = 4
}

public enum JobStatus
{
    Pending = 1,
    Running = 2,
    Completed = 3,
    Failed = 4,
    Cancelled = 5
}

public enum AnomalyStatus
{
    PendingReview = 1,
    Approved = 2,
    Rejected = 3
}

public enum ProductCondition
{
    New = 1,
    Refurbished = 2,
    Used = 3
}
