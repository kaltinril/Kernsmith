namespace KernSmith.Font.Tables;

public sealed record HeadTable(
    int UnitsPerEm,
    int XMin,
    int YMin,
    int XMax,
    int YMax,
    int IndexToLocFormat,
    long Created,
    long Modified);
