using AuditNode.Application.DTOs;

namespace AuditNode.Application.Interfaces;

/// <summary>
/// Service for handling inventory template generation and bulk import.
/// </summary>
public interface IInventoryImportService
{
    /// <summary>
    /// Generates an Excel template for inventory import with data validation.
    /// </summary>
    /// <returns>Byte array of the Excel file.</returns>
    byte[] GenerateTemplate();

    /// <summary>
    /// Processes a bulk import from an Excel stream.
    /// </summary>
    /// <param name="excelStream">The stream containing the Excel file.</param>
    /// <returns>A response DTO containing counts, errors, and conflicts.</returns>
    Task<ImportResponseDto> ImportInventoryAsync(Stream excelStream);
}
