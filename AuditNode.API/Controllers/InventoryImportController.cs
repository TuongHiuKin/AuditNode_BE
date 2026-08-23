using AuditNode.API.Errors;
using AuditNode.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AuditNode.API.Security;

namespace AuditNode.API.Controllers;

[Authorize]
[ApiController]
[Route("api/v1/inventory")]
public class InventoryImportController : ControllerBase
{
    public const long MaxImportBytes = 10 * 1024 * 1024;
    private const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private readonly IInventoryImportService _importService;

    public InventoryImportController(IInventoryImportService importService)
    {
        _importService = importService;
    }

    [HttpGet("import-template")]
    public IActionResult DownloadTemplate() =>
        File(_importService.GenerateTemplate(), ExcelContentType, "Inventory_Import_Template.xlsx");

    [WorkspaceMutation(ownerOrAdminOnly: true)]
    [HttpPost("import")]
    [RequestSizeLimit(MaxImportBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxImportBytes)]
    public async Task<IActionResult> ImportInventory(IFormFile file)
    {
        if (file is null || file.Length == 0)
            return BadRequest(Problem(400, "An .xlsx inventory file is required."));
        if (file.Length > MaxImportBytes)
            return BadRequest(Problem(400, $"Inventory files cannot exceed {MaxImportBytes / 1024 / 1024} MB."));
        if (!Path.GetExtension(file.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(file.ContentType, ExcelContentType, StringComparison.OrdinalIgnoreCase))
            return BadRequest(Problem(400, "Only .xlsx workbook content is supported."));

        try
        {
            await using var stream = file.OpenReadStream();
            var result = await _importService.ImportInventoryAsync(stream);
            if (result.Errors.Any(error => error.Type == "Transaction"))
                return StatusCode(500, Problem(500, "The inventory import could not be saved."));
            if (result.Conflicts.Count > 0)
                return Conflict(ProblemWithImport(409, "The inventory workbook contains conflicts.", result));
            if (result.Errors.Count > 0)
                return BadRequest(ProblemWithImport(400, "The inventory workbook is invalid.", result));
            return Ok(result);
        }
        catch (Exception)
        {
            return StatusCode(500, Problem(500, "The inventory import could not be completed."));
        }
    }

    private ProblemDetails Problem(int status, string title) =>
        ApiProblem.Create(ControllerContext.HttpContext, status, title);

    private ProblemDetails ProblemWithImport(int status, string title, object import)
    {
        var problem = Problem(status, title);
        problem.Extensions["import"] = import;
        return problem;
    }
}
