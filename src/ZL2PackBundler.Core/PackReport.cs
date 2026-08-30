using ZL2PackBundler.Core.Analysis;
using ZL2PackBundler.Core.Models;

namespace ZL2PackBundler.Core;

public sealed record PackReport(
    BundledPackType Type,
    PackFormat Format,
    string Name,
    string? McVersion,
    long PackZipBytes,
    long FinalApkBytes,
    string OutputPath,
    IReadOnlyList<OfflineItem> OfflineReport,
    IReadOnlyList<GuardWarning> Warnings,
    string? CertificateInfo);
