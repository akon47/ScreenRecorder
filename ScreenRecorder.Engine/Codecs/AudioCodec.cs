namespace MediaEncoder
{
    /// <summary>
    /// Audio codec selection. Member names are kept stable for saved-config round-trips.
    /// </summary>
    public enum AudioCodec
    {
        None = 0,
        Default = 1,
        Aac = 2,
        Mp3 = 3,
    }
}
