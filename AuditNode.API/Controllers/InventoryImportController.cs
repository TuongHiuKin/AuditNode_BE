using AuditNode.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuditNode.API.Controllers;

[Authorize]
[ApiController]
[Route("api/inventory")]
public class InventoryImportController : ControllerBase
{
    private readonly IInventoryImportService _importService;

    public InventoryImportController(IInventoryImportService importService)
    {
        _importService = importService;
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
    /// POST /api/inventory/import
    /// Processes the bulk import of topology inventory from an Excel file.
    /// </summary>
    [HttpPost("import")]
    public async Task<IActionResult> ImportInventory(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest("No file uploaded.");
        }

        if (!Path.GetExtension(file.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("Only .xlsx files are supported.");
        }

        using var stream = file.OpenReadStream();
        var result = await _importService.ImportInventoryAsync(stream);

        return Ok(result);
    }
}
