namespace PatientManagement.Core.Exceptions;

public class ForbiddenAccessException : Exception
{
    public ForbiddenAccessException(string message = "You do not have permission to access this resource.")
        : base(message) { }
}

public class BookingConflictException : Exception
{
    public BookingConflictException(string message)
        : base(message) { }
}
