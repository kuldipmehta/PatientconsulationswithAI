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

public class EmbeddingUnavailableException : Exception
{
    public EmbeddingUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException) { }
}
