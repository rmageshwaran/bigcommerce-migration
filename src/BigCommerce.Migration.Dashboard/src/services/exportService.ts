import * as XLSX from 'xlsx';
import jsPDF from 'jspdf';
import html2canvas from 'html2canvas';
import { saveAs } from 'file-saver';
import { format } from 'date-fns';

// Export types
export type ExportFormat = 'csv' | 'excel' | 'pdf';
export type ExportDataType = 'migrations' | 'entities' | 'errors' | 'performance' | 'charts' | 'custom';

// Export options interface
export interface ExportOptions {
  format: ExportFormat;
  filename?: string;
  includeTimestamp?: boolean;
  includeHeaders?: boolean;
  dateRange?: {
    start: Date;
    end: Date;
  };
  filters?: Record<string, unknown>;
  customData?: unknown[];
}

// Export result interface
export interface ExportResult {
  success: boolean;
  filename: string;
  format: ExportFormat;
  size?: string;
  recordCount?: number;
  error?: string;
}

// Migration data interface for exports
export interface MigrationExportData {
  migrationId: string;
  name: string;
  status: string;
  sourceStore: string;
  destinationStore: string;
  startTime: string;
  endTime?: string;
  duration?: string;
  totalEntities: number;
  processedEntities: number;
  successfulEntities: number;
  failedEntities: number;
  successRate: string;
  entitiesPerSecond: number;
  entityTypes: string;
  currentPhase?: string;
  errorCount: number;
}

// Entity progress data interface
export interface EntityExportData {
  migrationId: string;
  entityType: string;
  totalCount: number;
  processedCount: number;
  successCount: number;
  failureCount: number;
  progressPercentage: string;
  processingSpeed: number;
  estimatedTimeRemaining?: string;
  currentBatch?: number;
  totalBatches?: number;
  status: string;
  lastUpdated: string;
}

// Error data interface
export interface ErrorExportData {
  migrationId: string;
  entityType: string;
  entityId: string;
  errorCode: string;
  errorMessage: string;
  timestamp: string;
  severity: string;
  retryCount: number;
  resolved: boolean;
  resolvedBy?: string;
  resolvedAt?: string;
}

// Performance data interface
export interface PerformanceExportData {
  migrationId: string;
  timestamp: string;
  entitiesPerSecond: number;
  apiRequestsPerSecond: number;
  memoryUsage: string;
  cpuUsage: string;
  queueLength: number;
  activeConnections: number;
  responseTime: number;
  errorRate: string;
}

class ExportService {
  // Generate filename with timestamp
  private generateFilename(baseName: string, format: ExportFormat, includeTimestamp = true): string {
    const timestamp = includeTimestamp ? `_${format(new Date(), 'yyyy-MM-dd_HH-mm-ss')}` : '';
    const extension = format === 'excel' ? 'xlsx' : format;
    return `${baseName}${timestamp}.${extension}`;
  }

  // Format file size
  private formatFileSize(bytes: number): string {
    if (bytes === 0) return '0 Bytes';
    const k = 1024;
    const sizes = ['Bytes', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return `${parseFloat((bytes / Math.pow(k, i)).toFixed(2))} ${sizes[i]}`;
  }

  // Export migrations data
  public async exportMigrations(
    migrations: MigrationExportData[],
    options: ExportOptions
  ): Promise<ExportResult> {
    try {
      const filename = this.generateFilename(
        options.filename || 'migrations',
        options.format,
        options.includeTimestamp
      );

      let result: ExportResult;

      switch (options.format) {
        case 'csv':
          result = await this.exportToCSV(migrations, filename, options);
          break;
        case 'excel':
          result = await this.exportToExcel(migrations, filename, options);
          break;
        case 'pdf':
          result = await this.exportToPDF(migrations, filename, 'Migrations Report', options);
          break;
        default:
          throw new Error(`Unsupported export format: ${options.format}`);
      }

      return {
        ...result,
        recordCount: migrations.length,
      };
    } catch (error) {
      return {
        success: false,
        filename: '',
        format: options.format,
        error: error instanceof Error ? error.message : 'Unknown error occurred',
      };
    }
  }

  // Export entity progress data
  public async exportEntityProgress(
    entities: EntityExportData[],
    options: ExportOptions
  ): Promise<ExportResult> {
    try {
      const filename = this.generateFilename(
        options.filename || 'entity_progress',
        options.format,
        options.includeTimestamp
      );

      let result: ExportResult;

      switch (options.format) {
        case 'csv':
          result = await this.exportToCSV(entities, filename, options);
          break;
        case 'excel':
          result = await this.exportToExcel(entities, filename, options);
          break;
        case 'pdf':
          result = await this.exportToPDF(entities, filename, 'Entity Progress Report', options);
          break;
        default:
          throw new Error(`Unsupported export format: ${options.format}`);
      }

      return {
        ...result,
        recordCount: entities.length,
      };
    } catch (error) {
      return {
        success: false,
        filename: '',
        format: options.format,
        error: error instanceof Error ? error.message : 'Unknown error occurred',
      };
    }
  }

  // Export error data
  public async exportErrors(
    errors: ErrorExportData[],
    options: ExportOptions
  ): Promise<ExportResult> {
    try {
      const filename = this.generateFilename(
        options.filename || 'migration_errors',
        options.format,
        options.includeTimestamp
      );

      let result: ExportResult;

      switch (options.format) {
        case 'csv':
          result = await this.exportToCSV(errors, filename, options);
          break;
        case 'excel':
          result = await this.exportToExcel(errors, filename, options);
          break;
        case 'pdf':
          result = await this.exportToPDF(errors, filename, 'Error Report', options);
          break;
        default:
          throw new Error(`Unsupported export format: ${options.format}`);
      }

      return {
        ...result,
        recordCount: errors.length,
      };
    } catch (error) {
      return {
        success: false,
        filename: '',
        format: options.format,
        error: error instanceof Error ? error.message : 'Unknown error occurred',
      };
    }
  }

  // Export performance data
  public async exportPerformance(
    performance: PerformanceExportData[],
    options: ExportOptions
  ): Promise<ExportResult> {
    try {
      const filename = this.generateFilename(
        options.filename || 'performance_metrics',
        options.format,
        options.includeTimestamp
      );

      let result: ExportResult;

      switch (options.format) {
        case 'csv':
          result = await this.exportToCSV(performance, filename, options);
          break;
        case 'excel':
          result = await this.exportToExcel(performance, filename, options);
          break;
        case 'pdf':
          result = await this.exportToPDF(performance, filename, 'Performance Report', options);
          break;
        default:
          throw new Error(`Unsupported export format: ${options.format}`);
      }

      return {
        ...result,
        recordCount: performance.length,
      };
    } catch (error) {
      return {
        success: false,
        filename: '',
        format: options.format,
        error: error instanceof Error ? error.message : 'Unknown error occurred',
      };
    }
  }

  // Export chart as image/PDF
  public async exportChart(
    chartElement: HTMLElement,
    options: ExportOptions & { title?: string }
  ): Promise<ExportResult> {
    try {
      const filename = this.generateFilename(
        options.filename || 'chart',
        options.format,
        options.includeTimestamp
      );

      const canvas = await html2canvas(chartElement, {
        backgroundColor: '#ffffff',
        scale: 2, // Higher resolution
        useCORS: true,
        allowTaint: true,
      });

      if (options.format === 'pdf') {
        const pdf = new jsPDF({
          orientation: 'landscape',
          unit: 'mm',
          format: 'a4',
        });

        const imgWidth = 297; // A4 landscape width
        const imgHeight = (canvas.height * imgWidth) / canvas.width;

        // Add title if provided
        if (options.title) {
          pdf.setFontSize(16);
          pdf.text(options.title, 148.5, 20, { align: 'center' });
          pdf.addImage(canvas.toDataURL('image/png'), 'PNG', 0, 30, imgWidth, imgHeight);
        } else {
          pdf.addImage(canvas.toDataURL('image/png'), 'PNG', 0, 10, imgWidth, imgHeight);
        }

        // Add timestamp
        pdf.setFontSize(10);
        pdf.text(`Generated: ${format(new Date(), 'yyyy-MM-dd HH:mm:ss')}`, 10, 200);

        pdf.save(filename);
      } else {
        // Export as PNG
        canvas.toBlob((blob) => {
          if (blob) {
            saveAs(blob, filename.replace(/\.(csv|xlsx)$/, '.png'));
          }
        });
      }

      return {
        success: true,
        filename,
        format: options.format,
        size: this.formatFileSize(canvas.toDataURL().length),
      };
    } catch (error) {
      return {
        success: false,
        filename: '',
        format: options.format,
        error: error instanceof Error ? error.message : 'Failed to export chart',
      };
    }
  }

  // CSV export implementation
  private async exportToCSV(data: unknown[], filename: string, options: ExportOptions): Promise<ExportResult> {
    if (!data || data.length === 0) {
      throw new Error('No data to export');
    }

    const headers = Object.keys(data[0] as Record<string, unknown>);
    const csvContent = [
      options.includeHeaders !== false ? headers.join(',') : '',
      ...data.map(row => 
        headers.map(header => {
          const value = (row as Record<string, unknown>)[header];
          // Escape commas and quotes in CSV
          const stringValue = String(value || '');
          return stringValue.includes(',') || stringValue.includes('"') 
            ? `"${stringValue.replace(/"/g, '""')}"` 
            : stringValue;
        }).join(',')
      )
    ].filter(row => row).join('\n');

    const blob = new Blob([csvContent], { type: 'text/csv;charset=utf-8;' });
    saveAs(blob, filename);

    return {
      success: true,
      filename,
      format: 'csv',
      size: this.formatFileSize(blob.size),
    };
  }

  // Excel export implementation
  private async exportToExcel(data: unknown[], filename: string, options: ExportOptions): Promise<ExportResult> {
    if (!data || data.length === 0) {
      throw new Error('No data to export');
    }

    const workbook = XLSX.utils.book_new();
    const worksheet = XLSX.utils.json_to_sheet(data);

    // Auto-size columns
    const range = XLSX.utils.decode_range(worksheet['!ref'] || 'A1');
    const columnWidths: { wch: number }[] = [];
    
    for (let col = range.s.c; col <= range.e.c; col++) {
      let maxWidth = 10;
      for (let row = range.s.r; row <= range.e.r; row++) {
        const cellAddress = XLSX.utils.encode_cell({ r: row, c: col });
        const cell = worksheet[cellAddress];
        if (cell && cell.v) {
          maxWidth = Math.max(maxWidth, String(cell.v).length);
        }
      }
      columnWidths.push({ wch: Math.min(maxWidth + 2, 50) });
    }
    worksheet['!cols'] = columnWidths;

    // Add metadata sheet
    const metadataSheet = XLSX.utils.json_to_sheet([
      { Property: 'Generated', Value: format(new Date(), 'yyyy-MM-dd HH:mm:ss') },
      { Property: 'Record Count', Value: data.length },
      { Property: 'Export Format', Value: 'Excel (XLSX)' },
    ]);

    XLSX.utils.book_append_sheet(workbook, worksheet, 'Data');
    XLSX.utils.book_append_sheet(workbook, metadataSheet, 'Metadata');

    const excelBuffer = XLSX.write(workbook, { bookType: 'xlsx', type: 'array' });
    const blob = new Blob([excelBuffer], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' });
    saveAs(blob, filename);

    return {
      success: true,
      filename,
      format: 'excel',
      size: this.formatFileSize(blob.size),
    };
  }

  // PDF export implementation
  private async exportToPDF(
    data: unknown[],
    filename: string,
    title: string,
    options: ExportOptions
  ): Promise<ExportResult> {
    if (!data || data.length === 0) {
      throw new Error('No data to export');
    }

    const pdf = new jsPDF({
      orientation: 'landscape',
      unit: 'mm',
      format: 'a4',
    });

    const pageWidth = pdf.internal.pageSize.getWidth();
    const pageHeight = pdf.internal.pageSize.getHeight();
    const margin = 20;
    const contentWidth = pageWidth - (margin * 2);

    // Title
    pdf.setFontSize(20);
    pdf.text(title, pageWidth / 2, margin, { align: 'center' });

    // Metadata
    pdf.setFontSize(10);
    const metadataY = margin + 15;
    pdf.text(`Generated: ${format(new Date(), 'yyyy-MM-dd HH:mm:ss')}`, margin, metadataY);
    pdf.text(`Records: ${data.length}`, pageWidth - margin - 50, metadataY);

    // Table data
    const headers = Object.keys(data[0] as Record<string, unknown>);
    const rows = data.map(row => 
      headers.map(header => String((row as Record<string, unknown>)[header] || ''))
    );

    // Simple table implementation
    const tableY = metadataY + 10;
    const rowHeight = 6;
    const colWidth = contentWidth / headers.length;

    // Headers
    pdf.setFontSize(8);
    pdf.setFont(undefined, 'bold');
    headers.forEach((header, index) => {
      pdf.text(header, margin + (index * colWidth), tableY);
    });

    // Data rows
    pdf.setFont(undefined, 'normal');
    let currentY = tableY + rowHeight;

    rows.forEach((row, rowIndex) => {
      if (currentY > pageHeight - margin) {
        pdf.addPage();
        currentY = margin;
      }

      row.forEach((cell, colIndex) => {
        const cellText = cell.length > 20 ? cell.substring(0, 17) + '...' : cell;
        pdf.text(cellText, margin + (colIndex * colWidth), currentY);
      });

      currentY += rowHeight;
    });

    pdf.save(filename);

    const pdfSize = (pdf as any).output('arraybuffer').byteLength;
    return {
      success: true,
      filename,
      format: 'pdf',
      size: this.formatFileSize(pdfSize),
    };
  }

  // Generate sample data for testing
  public generateSampleMigrationData(): MigrationExportData[] {
    return [
      {
        migrationId: 'mig_001',
        name: 'Products Migration - Store A to B',
        status: 'completed',
        sourceStore: 'store-a.mybigcommerce.com',
        destinationStore: 'store-b.mybigcommerce.com',
        startTime: '2025-01-10 10:00:00',
        endTime: '2025-01-10 12:30:00',
        duration: '2h 30m',
        totalEntities: 1500,
        processedEntities: 1500,
        successfulEntities: 1485,
        failedEntities: 15,
        successRate: '99.0%',
        entitiesPerSecond: 1.67,
        entityTypes: 'Products, Variants, Images',
        currentPhase: 'completed',
        errorCount: 15,
      },
      {
        migrationId: 'mig_002',
        name: 'Categories Migration - Store C to D',
        status: 'running',
        sourceStore: 'store-c.mybigcommerce.com',
        destinationStore: 'store-d.mybigcommerce.com',
        startTime: '2025-01-10 14:00:00',
        duration: '45m',
        totalEntities: 250,
        processedEntities: 180,
        successfulEntities: 175,
        failedEntities: 5,
        successRate: '97.2%',
        entitiesPerSecond: 2.1,
        entityTypes: 'Categories',
        currentPhase: 'processing',
        errorCount: 5,
      },
    ];
  }
}

// Export singleton instance
export const exportService = new ExportService();
export default exportService; 