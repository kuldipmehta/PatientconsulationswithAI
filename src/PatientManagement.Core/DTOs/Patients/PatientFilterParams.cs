namespace PatientManagement.Core.DTOs.Patients;

public class PatientFilterParams
{
    private const int MaxPageSize = 100;
    private int _pageSize = 10;

    public string? SearchTerm { get; set; }
    public string? Gender { get; set; }
    public int? MinAge { get; set; }
    public int? MaxAge { get; set; }
    public string? SortBy { get; set; } = "CreatedAt";
    public bool SortDescending { get; set; } = true;

    public int PageNumber { get; set; } = 1;
    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value > MaxPageSize ? MaxPageSize : (value < 1 ? 1 : value);
    }
}
