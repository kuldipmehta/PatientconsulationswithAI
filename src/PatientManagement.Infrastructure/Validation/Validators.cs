using FluentValidation;
using Microsoft.AspNetCore.Http;
using PatientManagement.Core.DTOs.Ai;
using PatientManagement.Core.DTOs.Auth;
using PatientManagement.Core.DTOs.Consultations;
using PatientManagement.Core.DTOs.Patients;

namespace PatientManagement.Infrastructure.Validation;

public static class FileUploadValidator
{
    public const long MaxPictureBytes = 2 * 1024 * 1024; // 2 MB

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp"
    };

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/jpg", "image/png", "image/webp"
    };

    public static bool IsValidImage(IFormFile? file, out string error)
    {
        error = string.Empty;
        if (file == null)
        {
            return true;
        }

        if (file.Length <= 0)
        {
            error = "Uploaded picture file cannot be empty.";
            return false;
        }

        if (file.Length > MaxPictureBytes)
        {
            error = $"Profile picture size exceeds the maximum allowed limit of {MaxPictureBytes / (1024 * 1024)} MB.";
            return false;
        }

        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(ext) || !AllowedExtensions.Contains(ext))
        {
            error = "Only .jpg, .jpeg, .png, and .webp image files are allowed.";
            return false;
        }

        if (!AllowedContentTypes.Contains(file.ContentType))
        {
            error = "Unsupported image MIME content type.";
            return false;
        }

        if (!HasValidImageMagicBytes(file, ext))
        {
            error = "Uploaded file header does not match a valid JPEG, PNG, or WEBP image.";
            return false;
        }

        return true;
    }

    private static bool HasValidImageMagicBytes(IFormFile file, string ext)
    {
        try
        {
            using var stream = file.OpenReadStream();
            Span<byte> header = stackalloc byte[12];
            int read = stream.Read(header);
            if (read < 4)
            {
                return false;
            }

            // JPEG: FF D8 FF
            if (ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
            {
                return header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
            }

            // PNG: 89 50 4E 47
            if (ext.Equals(".png", StringComparison.OrdinalIgnoreCase))
            {
                return header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47;
            }

            // WEBP: RIFF....WEBP
            if (ext.Equals(".webp", StringComparison.OrdinalIgnoreCase) && read >= 12)
            {
                return header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46
                    && header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }
}

public class CreatePatientDtoValidator : AbstractValidator<CreatePatientDto>
{
    public CreatePatientDtoValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(50);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(100);
        RuleFor(x => x.Age).InclusiveBetween(0, 130);
        RuleFor(x => x.Gender).NotEmpty().Must(g => new[] { "Male", "Female", "Other" }.Contains(g, StringComparer.OrdinalIgnoreCase))
            .WithMessage("Gender must be Male, Female, or Other.");
        RuleFor(x => x.MedicalNotes).MaximumLength(1000);
        RuleFor(x => x.Picture)
            .Custom((file, ctx) =>
            {
                if (!FileUploadValidator.IsValidImage(file, out var error))
                {
                    ctx.AddFailure("Picture", error);
                }
            });
    }
}

public class UpdatePatientDtoValidator : AbstractValidator<UpdatePatientDto>
{
    public UpdatePatientDtoValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(50);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(100);
        RuleFor(x => x.Age).InclusiveBetween(0, 130);
        RuleFor(x => x.Gender).NotEmpty().Must(g => new[] { "Male", "Female", "Other" }.Contains(g, StringComparer.OrdinalIgnoreCase))
            .WithMessage("Gender must be Male, Female, or Other.");
        RuleFor(x => x.Picture)
            .Custom((file, ctx) =>
            {
                if (!FileUploadValidator.IsValidImage(file, out var error))
                {
                    ctx.AddFailure("Picture", error);
                }
            });
    }
}

public class CreateConsultationDtoValidator : AbstractValidator<CreateConsultationDto>
{
    public CreateConsultationDtoValidator()
    {
        RuleFor(x => x.PatientId).GreaterThan(0);
        RuleFor(x => x.DoctorName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Diagnosis).NotEmpty().MaximumLength(250);
        RuleFor(x => x.DurationMinutes).InclusiveBetween(10, 240);
        RuleFor(x => x.Fee).GreaterThanOrEqualTo(0).When(x => x.Fee.HasValue);
    }
}

public class UpdateConsultationDtoValidator : AbstractValidator<UpdateConsultationDto>
{
    public UpdateConsultationDtoValidator()
    {
        RuleFor(x => x.DoctorName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Diagnosis).NotEmpty().MaximumLength(250);
        RuleFor(x => x.DurationMinutes).InclusiveBetween(10, 240);
        RuleFor(x => x.Fee).GreaterThanOrEqualTo(0).When(x => x.Fee.HasValue);
    }
}

public class AiChatRequestDtoValidator : AbstractValidator<AiChatRequestDto>
{
    public AiChatRequestDtoValidator()
    {
        RuleFor(x => x.Message).NotEmpty().MaximumLength(2000);
    }
}

public class AiTriageRequestDtoValidator : AbstractValidator<AiTriageRequestDto>
{
    public AiTriageRequestDtoValidator()
    {
        RuleFor(x => x.Symptoms).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.PatientAge).InclusiveBetween(0, 130).When(x => x.PatientAge.HasValue);
    }
}

public class SetPasswordRequestDtoValidator : AbstractValidator<SetPasswordRequestDto>
{
    public SetPasswordRequestDtoValidator()
    {
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8).MaximumLength(100);
    }
}
