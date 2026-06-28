using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace AuditNode.API.Controllers;

[Authorize]
[ApiController]
[Route("api/v1/inventory")]
public class InventoryImportController : ControllerBase
{
    private readonly IInventoryImportService _importService;
    private readonly ILogger<InventoryImportController> _logger;

    public InventoryImportController(IInventoryImportService importService, ILogger<InventoryImportController> logger)
    {
        _importService = importService;
        _logger = logger;
    }

    /// <summary>
    /// GET /api/inventory/import-template
    /// Downloads the Excel template for inventory import.
    /// </summary>
    [HttpGet("import-template")]
    public IActionResult DownloadTemplate()
    {
        var content = _importService.GenerateTemplate();
        return File(
            content, 
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", 
            "Inventory_Import_Template.xlsx"
        );
    }

    /// <summary>
    /// POST /api/v1/inventory/bulk-import
    /// Processes the bulk import of topology inventory from an Excel file.
    /// </summary>
    [HttpPost("bulk-import")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> BulkImport([FromForm] IFormFile file)
    {
        _logger.LogInformation("[DEBUG IMPORT] Endpoint hit. File received: {FileName}, Length: {Length}", file?.FileName, file?.Length);

        if (file == null || file.Length == 0)
        {
            return BadRequest("No file uploaded.");
        }

        if (!Path.GetExtension(file.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("Only .xlsx files are supported.");
        }

        var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("User ID not found in token.");
        }

        using var stream = file.OpenReadStream();
        var result = await _importService.ImportInventoryAsync(stream, currentUserId);

        return Ok(result);
    }
}
