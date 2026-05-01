using SahilAI.Application.DTOs;

namespace SahilAI.Application.Interfaces;

public interface IReviewApprovalService
{
    Task<ApprovalResultDto> ApproveAsync(int invoiceId, string approvedBy);
}
