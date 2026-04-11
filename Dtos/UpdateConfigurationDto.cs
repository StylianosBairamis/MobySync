using MobySync.Models;

namespace MobySync.Dtos;

public class UpdateConfigurationDto
{
    public List<ServiceConfiguration>? Services { get; set; }
    public GenericSettings? GenericSettings { get; set; }
}
