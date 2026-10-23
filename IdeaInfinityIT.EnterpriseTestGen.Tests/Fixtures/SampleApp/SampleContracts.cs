using System;
using System.Threading.Tasks;

namespace IdeaInfinityIT.EnterpriseTestGen.Tests.Fixtures.SampleApp
{
    public class SampleOrder
    {
        public long Id { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Status { get; set; } = "Pending";
    }

    public interface ISampleRepository
    {
        Task<SampleOrder?> GetOrderByIdAsync(long orderId);
        Task<bool> SaveOrderAsync(SampleOrder order);
        bool Exists(long orderId);
    }

    public interface ISampleNotificationService
    {
        Task SendNotificationAsync(string recipient, string message);
    }
}
