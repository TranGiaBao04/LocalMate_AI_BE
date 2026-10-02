namespace LocalMateAI.Application.DTOs.Email;

public sealed record SingleItineraryPaymentReceiptEmailModel(
    string FullName, string OrderCode, string Amount, string PaidAt);
