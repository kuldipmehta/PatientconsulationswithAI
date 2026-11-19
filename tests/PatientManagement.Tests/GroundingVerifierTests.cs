using Microsoft.Extensions.Logging.Abstractions;
using PatientManagement.Core.Enums;
using PatientManagement.Infrastructure.Services.Ai;
using Xunit;

namespace PatientManagement.Tests;

public class GroundingVerifierTests
{
    private readonly ClinicalGroundingVerifierService _verifier = new(NullLogger<ClinicalGroundingVerifierService>.Instance);

    [Fact]
    public async Task VerifyAsync_EmptyText_ReturnsGroundedResult()
    {
        var result = await _verifier.VerifyAsync(string.Empty, new List<string> { "Patient record" });

        Assert.True(result.IsGrounded);
        Assert.Equal(1.0, result.GroundingScore);
        Assert.False(result.RequiresCorrection);
        Assert.Empty(result.Claims);
        Assert.Empty(result.HallucinationFlags);
    }

    [Fact]
    public async Task VerifyAsync_ExactSourceMatchedMetrics_ReturnsFullyGrounded()
    {
        var source = new List<string>
        {
            "Patient vital signs: BP 120/80 mmHg, pulse 72 bpm, HbA1c 6.5%.",
            "Current active medications: Lisinopril 20mg once daily."
        };

        var generated = "The patient has a documented BP of 120/80 mmHg with a heart rate of 72 bpm and HbA1c 6.5%. The patient is taking Lisinopril.";

        var result = await _verifier.VerifyAsync(generated, source);

        Assert.True(result.IsGrounded);
        Assert.Equal(1.0, result.GroundingScore);
        Assert.False(result.RequiresCorrection);
        Assert.NotEmpty(result.Claims);
        Assert.All(result.Claims, c => Assert.Equal(ClaimStatus.Supported, c.Status));
        Assert.Empty(result.HallucinationFlags);
    }

    [Fact]
    public async Task VerifyAsync_HallucinatedLabMetric_FlagsUngrounded()
    {
        var source = new List<string>
        {
            "Patient blood glucose is 110 mg/dL and HbA1c is 5.8%."
        };

        var generated = "Recent laboratory review indicates patient HbA1c is 8.5% with fasting glucose of 190 mg/dL.";

        var result = await _verifier.VerifyAsync(generated, source);

        Assert.False(result.IsGrounded);
        Assert.True(result.RequiresCorrection);
        Assert.Equal(0.0, result.GroundingScore);
        Assert.Contains(result.HallucinationFlags, f => f.Contains("UNGROUNDED LAB / METRIC"));
        Assert.All(result.Claims, c => Assert.Equal(ClaimStatus.Ungrounded, c.Status));
    }

    [Fact]
    public async Task VerifyAsync_ContradictedAllergyAssertion_FlagsContradicted()
    {
        var source = new List<string>
        {
            "Patient has a documented severe allergy to Penicillin with clinical history of anaphylaxis."
        };

        var generated = "Clinical assessment confirms patient denies allergies and has no known drug allergies.";

        var result = await _verifier.VerifyAsync(generated, source);

        Assert.False(result.IsGrounded);
        Assert.True(result.RequiresCorrection);
        Assert.Contains(result.Claims, c => c.Status == ClaimStatus.Contradicted);
        Assert.Contains(result.HallucinationFlags, f => f.Contains("CONTRADICTION"));
    }

    [Fact]
    public async Task VerifyAsync_UngroundedMedication_FlagsUngrounded()
    {
        var source = new List<string>
        {
            "Patient is managed for hypertension and takes Metformin for type 2 diabetes."
        };

        var generated = "The patient is taking Warfarin daily for anticoagulant therapy.";

        var result = await _verifier.VerifyAsync(generated, source);

        Assert.False(result.IsGrounded);
        Assert.True(result.RequiresCorrection);
        Assert.Contains(result.Claims, c => c.Status == ClaimStatus.Ungrounded && c.ClaimText.Contains("Warfarin"));
        Assert.Contains(result.HallucinationFlags, f => f.Contains("UNGROUNDED MEDICATION"));
    }

    [Fact]
    public async Task VerifyAsync_MixedGroundedAndUngrounded_CalculatesCorrectScore()
    {
        var source = new List<string>
        {
            "Active medications: Metformin 500mg daily. BP recorded as 130/85 mmHg."
        };

        // Metformin is supported; 9.2% is ungrounded
        var generated = "Patient is currently taking Metformin. Most recent lab shows HbA1c of 9.2%.";

        var result = await _verifier.VerifyAsync(generated, source);

        Assert.False(result.IsGrounded);
        Assert.True(result.RequiresCorrection);
        Assert.Equal(0.5, result.GroundingScore); // 1 of 2 claims supported
        Assert.Contains(result.Claims, c => c.Status == ClaimStatus.Supported);
        Assert.Contains(result.Claims, c => c.Status == ClaimStatus.Ungrounded);
    }
}
