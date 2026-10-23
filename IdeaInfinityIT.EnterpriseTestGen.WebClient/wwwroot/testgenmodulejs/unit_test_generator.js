// Global State
let currentProjectAnalysis = null;
let currentMultiProjectAnalysis = null;
let currentGeneratedCode = '';
let currentTargetClassName = '';
let allDiscoveredClasses = [];
let allGeneratedTestCases = [];
let currentReviewCases = [];
let activeClassFilter = 'all';
let activeCaseFilter = 'all';
let activeProjectFilter = 'all';
let currentSearchMatchIndex = 0;
let lastCodeSearchQuery = '';
let currentBrowseTargetInputId = 'targetPathInput';
let scenarioChartInstance = null;
let coverageChartInstance = null;

// Track files fetched from user's system
let selectedWebFolderFiles = null;
let selectedApiFolderFiles = null;
let selectedSingleFolderFiles = null;
let selectedExistingTestFolderFiles = null;

let currentAiSettings = {
    provider: 'LocalHeuristic',
    endpoint: 'http://localhost:11434',
    apiKey: '',
    model: 'llama3',
    temperature: 0.2
};

function browseOrFetchFor(inputId, fileInputId) {
    currentBrowseTargetInputId = inputId;
    const currentVal = $(`#${inputId}`).val().trim();
    if (currentVal && !currentVal.startsWith('[Folder]:')) {
        openDirectoryBrowserModal(currentVal);
    } else {
        $(`#${fileInputId}`).click();
    }
}

$(document).ready(function () {
    // Initialize Highlight.js
    if (window.hljs) {
        hljs.highlightAll();
    }

    // Initialize Dashboard Charts
    initDashboardCharts();

    // Bind Dual Projects Analyze Button
    $('#btnAnalyzeDualProjects').on('click', function () {
        analyzeDualProjects();
    });

    // Bind Single Project Analyze Button
    $('#btnAnalyzeProject').on('click', function () {
        analyzeTargetProject();
    });

    // File Selection Listeners for Dual Mode
    $('#webFolderInput').on('change', function (e) {
        const files = e.target.files;
        if (files && files.length > 0) {
            selectedWebFolderFiles = files;
            const root = files[0].webkitRelativePath ? files[0].webkitRelativePath.split('/')[0] : 'WebProject';
            $('#webUploadNotice').html(`<i class="ri-check-line text-success"></i> Folder: <strong>${escapeHtml(root)}</strong> (${files.length} files)`);
            $('#webProjectPathInput').val(root);
        }
    });

    $('#webZipInput').on('change', function () {
        const file = this.files[0];
        if (file) {
            $('#webUploadNotice').html(`<i class="ri-file-zip-line text-primary"></i> ZIP: <strong>${escapeHtml(file.name)}</strong>`);
            $('#webProjectPathInput').val(file.name);
        }
    });

    $('#apiFolderInput').on('change', function (e) {
        const files = e.target.files;
        if (files && files.length > 0) {
            selectedApiFolderFiles = files;
            const root = files[0].webkitRelativePath ? files[0].webkitRelativePath.split('/')[0] : 'ApiProject';
            $('#apiUploadNotice').html(`<i class="ri-check-line text-success"></i> Folder: <strong>${escapeHtml(root)}</strong> (${files.length} files)`);
            $('#apiProjectPathInput').val(root);
        }
    });

    $('#apiZipInput').on('change', function () {
        const file = this.files[0];
        if (file) {
            $('#apiUploadNotice').html(`<i class="ri-file-zip-line text-success"></i> ZIP: <strong>${escapeHtml(file.name)}</strong>`);
            $('#apiProjectPathInput').val(file.name);
        }
    });

    $('#existingTestFolderInput').on('change', function (e) {
        const files = e.target.files;
        if (files && files.length > 0) {
            selectedExistingTestFolderFiles = files;
            const root = files[0].webkitRelativePath ? files[0].webkitRelativePath.split('/')[0] : 'TestProject';
            $('#existingTestPathInput').val(root);
        }
    });

    // Single Project File Listeners
    $('#projectZipInput').on('change', function () {
        const file = this.files[0];
        if (file) {
            $('#targetPathInput').val(file.name);
            $('#fileUploadStatusNotice').removeClass('d-none').html(`<i class="ri-file-zip-line text-primary me-1"></i>Project ZIP loaded: <strong>${escapeHtml(file.name)}</strong> (${(file.size / (1024 * 1024)).toFixed(2)} MB)`);
            $('#detectedRoleBadge').text('ZIP Archive Loaded');
        }
    });

    $('#csharpFilesInput').on('change', function () {
        const files = this.files;
        if (files && files.length > 0) {
            const count = files.length;
            const names = Array.from(files).map(f => f.name).slice(0, 2).join(', ');
            const extra = count > 2 ? ` and ${count - 2} more` : '';
            $('#targetPathInput').val(`${count} .cs files`);
            $('#fileUploadStatusNotice').removeClass('d-none').html(`<i class="ri-file-code-line text-info me-1"></i>Selected ${count} .cs file(s): <strong>${names}${extra}</strong>`);
            $('#detectedRoleBadge').text(`${count} Files Loaded`);
        }
    });

    // Folder Upload Change Event (Single Mode)
    $('#folderUploadInput').on('change', function (e) {
        const files = e.target.files;
        if (files && files.length > 0) {
            selectedSingleFolderFiles = files;
            const rootFolder = files[0].webkitRelativePath ? files[0].webkitRelativePath.split('/')[0] : 'UploadedFolder';
            $('#fileUploadStatusNotice').removeClass('d-none').html(
                `<i class="ri-folder-check-line me-1"></i>Selected Folder: <strong>${escapeHtml(rootFolder)}</strong> (${files.length} total files detected). Click 'Scan Project' to analyze.`
            );
            $('#targetPathInput').val(rootFolder);
        }
    });

    // Search Engine Input for filtering target classes
    $('#classSearchInput').on('input keyup', function () {
        filterClassList();
    });

    // Clear Search Input Button
    $('#btnClearClassSearch').on('click', function () {
        $('#classSearchInput').val('');
        filterClassList();
        $('#classSearchInput').focus();
    });

    // Category Filter Chips
    $('#classCategoryFilters').on('click', '.filter-chip', function () {
        $('#classCategoryFilters .filter-chip').removeClass('btn-primary active-filter').addClass('btn-outline-secondary');
        $(this).removeClass('btn-outline-secondary').addClass('btn-primary active-filter');
        activeClassFilter = $(this).data('filter');
        filterClassList();
    });

    // Class Selector Dropdown Change
    $('#classSelectorDropdown').on('change', function () {
        const selected = $(this).val();
        updateSelectedClassRoleMetadata(selected);
        filterAndRenderReviewCases();
    });



    // Test Cases Search & Filter Listeners (Step 8 Review Matrix)
    $('#testCaseSearchInput').on('input keyup', function () {
        filterAndRenderReviewCases();
    });

    $('#btnClearCaseSearch').on('click', function () {
        $('#testCaseSearchInput').val('');
        filterAndRenderReviewCases();
        $('#testCaseSearchInput').focus();
    });

    $('.case-filter-chip').on('click', function () {
        $('.case-filter-chip').removeClass('btn-primary active-filter').addClass('btn-outline-secondary');
        $(this).removeClass('btn-outline-secondary').addClass('btn-primary active-filter');
        activeCaseFilter = $(this).data('category') || 'all';
        activeProjectFilter = $(this).data('project') || 'all';
        filterAndRenderReviewCases();
    });

    // Action Buttons
    $('#btnCopyCode').on('click', function () {
        copyGeneratedCodeToClipboard();
    });

    $('#btnDownloadSingleCode').on('click', function () {
        downloadSingleGeneratedCsFile();
    });

    $('#btnDownloadZip').on('click', function () {
        downloadCompleteProjectTestsZip();
    });

    $('#btnSaveToProject').on('click', function () {
        saveGeneratedCodeToDisk();
    });

    $('#btnExportCasesCsv').on('click', function () {
        exportTestCasesToCsv();
    });

    $('#btnLoadSampleYaml').on('click', function () {
        const sampleYaml = `targetClass: PurchaseOrderBaseController
targetMethod: GetAllPurchaseOrderAsync
customTestCases:
  - name: StatusApproved_ShouldReturnSuccess
    given:
      status: 'A'
      fromDate: '01-01-2025'
      toDate: '31-01-2025'
    expected:
      statusCode: 200
      isSuccess: true
  - name: InvalidDateRange_ShouldHandleDefensively
    given:
      status: 'A'
      fromDate: '31-01-2025'
      toDate: '01-01-2025'
    expected:
      isSuccess: false`;
        $('#customYamlEditor').val(sampleYaml);
        Swal.fire({
            icon: 'info',
            title: 'Sample YAML Loaded',
            text: 'Loaded sample Arrange/Expected test scenarios for PurchaseOrderBaseController.',
            timer: 1500,
            showConfirmButton: false
        });
    });

    // Button: Run Tests
    $('#btnExecuteDotnetTest').on('click', function () {
        executeDotnetTests();
    });
});

/**
 * =========================================================================
 * Ultra-Fast JSZip In-Memory Compression (Bypasses multipart 1024 limits)
 * =========================================================================
 */
async function packageFolderFilesWithJSZip(files, onProgress) {
    if (typeof JSZip === 'undefined') {
        console.warn('JSZip not found in browser context.');
        return null;
    }
    const zip = new JSZip();
    let count = 0;
    for (let i = 0; i < files.length; i++) {
        const file = files[i];
        const rel = file.webkitRelativePath || file.name;
        const norm = rel.toLowerCase().replace(/\\/g, '/');

        // Ignore build outputs, version control, and temporary folders
        if (norm.includes('/bin/') || norm.includes('/obj/') ||
            norm.includes('/.git/') || norm.includes('/.vs/') ||
            norm.includes('/node_modules/') || norm.includes('/dist/')) {
            continue;
        }

        // Only upload relevant C# code and solution artifacts
        if (norm.endsWith('.cs') || norm.endsWith('.csproj') || norm.endsWith('.sln') || norm.endsWith('.json')) {
            zip.file(rel, file);
            count++;
        }
    }

    if (count === 0) return null;
    if (onProgress) onProgress(`Compressing ${count} source files...`);
    const blob = await zip.generateAsync({ type: 'blob', compression: 'DEFLATE', compressionOptions: { level: 6 } });
    return { blob, count };
}

/**
 * Fallback: Append individual files
 */
function appendFolderFilesToFormData(formData) {
    const folderFiles = getActiveFolderFiles();
    if (!folderFiles || folderFiles.length === 0) return 0;

    let addedCount = 0;
    for (let i = 0; i < folderFiles.length; i++) {
        const file = folderFiles[i];
        const rel = file.webkitRelativePath || file.name;
        const normRel = rel.toLowerCase().replace(/\\/g, '/');

        if (normRel.includes('/bin/') || normRel.includes('/obj/') ||
            normRel.includes('/.git/') || normRel.includes('/.vs/') ||
            normRel.includes('/node_modules/') || normRel.includes('/dist/')) {
            continue;
        }

        if (normRel.endsWith('.cs') || normRel.endsWith('.csproj') || normRel.endsWith('.sln') || normRel.endsWith('.json')) {
            formData.append('folderFiles', file);
            formData.append('folderRelativePaths', rel);
            addedCount++;
        }
    }
    return addedCount;
}

function getActiveFolderFiles() {
    if (selectedSingleFolderFiles && selectedSingleFolderFiles.length > 0) return selectedSingleFolderFiles;
    const f1 = $('#folderUploadInput')[0]?.files;
    if (f1 && f1.length > 0) return f1;
    const f2 = $('#browserNativeFolderInput')[0]?.files;
    if (f2 && f2.length > 0) return f2;
    return null;
}



/**
 * =========================================================================
 * Dual Projects Analysis: Web Application + Core API + Existing Tests
 * =========================================================================
 */
async function analyzeDualProjects() {
    const formData = new FormData();
    const webPath = $('#webProjectPathInput').val().trim();
    const apiPath = $('#apiProjectPathInput').val().trim();
    const existingTestPath = $('#existingTestPathInput').val().trim();

    const webFolder = selectedWebFolderFiles || $('#webFolderInput')[0]?.files;
    const webZip = $('#webZipInput')[0]?.files[0];
    const apiFolder = selectedApiFolderFiles || $('#apiFolderInput')[0]?.files;
    const apiZip = $('#apiZipInput')[0]?.files[0];

    // Check inputs
    if (!webPath && !apiPath && (!webFolder || webFolder.length === 0) && (!apiFolder || apiFolder.length === 0) && !webZip && !apiZip) {
        Swal.fire('Input Required', 'Please fetch or enter project folders for Web Client and Core API.', 'warning');
        return;
    }

    Swal.fire({
        title: 'Scanning Web & API Architecture...',
        html: '<div class="spinner-border text-primary" role="status"></div><div class="mt-2 text-muted small">Roslyn is parsing Web controllers, Core API endpoints, and existing xUnit test cases...</div>',
        allowOutsideClick: false,
        showConfirmButton: false
    });

    try {
        // Package Web folder with JSZip if uploaded
        if (webFolder && webFolder.length > 0) {
            const packedWeb = await packageFolderFilesWithJSZip(webFolder);
            if (packedWeb) formData.append('webZip', packedWeb.blob, 'WebProject.zip');
        } else if (webZip) {
            formData.append('webZip', webZip);
        }
        if (webPath) formData.append('webPath', webPath);

        // Package API folder with JSZip if uploaded
        if (apiFolder && apiFolder.length > 0) {
            const packedApi = await packageFolderFilesWithJSZip(apiFolder);
            if (packedApi) formData.append('apiZip', packedApi.blob, 'ApiProject.zip');
        } else if (apiZip) {
            formData.append('apiZip', apiZip);
        }
        if (apiPath) formData.append('apiPath', apiPath);

        if (existingTestPath) formData.append('existingTestPath', existingTestPath);

        // Optional BRD or custom YAML
        const brdFile = $('#brdFileInput')[0]?.files[0];
        const customYaml = $('#customYamlEditor').val().trim();
        if (brdFile) formData.append('brdFile', brdFile);
        if (customYaml) formData.append('customYaml', customYaml);

        $.ajax({
            url: '/UnitTestGenerator/AnalyzeDualProjects',
            type: 'POST',
            data: formData,
            contentType: false,
            processData: false,
            success: function (res) {
                Swal.close();
                if (res.success && res.result) {
                    const data = res.result;
                    currentMultiProjectAnalysis = data;
                    allDiscoveredClasses = data.allDiscoveredClasses || [];
                    currentReviewCases = data.reviewTestCases || [];

                    // Update UI Badges & Metadata
                    $('#projectDiscoverySection').removeClass('d-none');
                    $('#detectedRoleBadge').text(data.solutionName || 'Multi-Project Solution');

                    // Populate Class Dropdown
                    renderClassOptions(allDiscoveredClasses);

                    // Render Step 8 Review Test Cases Matrix
                    renderReviewTestCasesTable(currentReviewCases);

                    // Render Web-to-API Mappings Table
                    renderWebToApiMappingsTable(data.webToApiMappings || []);

                    // Update Dashboard Summary Cards
                    $('#dashProjectsCount').text(2);
                    $('#dashClassesCount').text(data.totalClasses);
                    $('#dashSourceFilesLabel').text(`${data.totalSourceFiles} Files`);
                    $('#dashMethodsCount').text(data.totalMethods);
                    $('#dashTestCasesCount').text(data.totalSuggestedCases);
                    $('#dashApprovedBadge').text(`${data.reviewTestCases?.filter(c => c.isApproved).length || 0} Approved`);

                    // Update Existing Tests Status
                    if (data.existingTestProject && data.existingTestProject.totalTestMethods > 0) {
                        $('#btnUpdateExistingSuite').removeClass('d-none');
                        Swal.fire({
                            icon: 'success',
                            title: 'Dual Projects Scanned Successfully!',
                            html: `<div>Detected <strong>${data.totalClasses}</strong> classes across Web & API.</div>
                                   <div class="mt-2 text-success"><i class="ri-check-double-line"></i> Found <strong>${data.existingTestProject.totalTestMethods}</strong> existing unit tests in <em>${data.existingTestProject.projectName}</em>.</div>
                                   <div class="mt-1 small text-muted">Review synthesized scenarios in <strong>'Test Cases Review (Step 8)'</strong> tab below.</div>`,
                            timer: 3500,
                            showConfirmButton: false
                        });
                    } else {
                        Swal.fire({
                            icon: 'success',
                            title: 'Dual Projects Scanned Successfully!',
                            html: `Detected <strong>${data.totalClasses}</strong> classes across Web & API. Review synthesized test scenarios in <strong>'Test Cases Review (Step 8)'</strong> tab below.`,
                            timer: 2500,
                            showConfirmButton: false
                        });
                    }

                    // Update Visual Charts
                    updateDashboardCharts(currentReviewCases);

                    // Switch to Step 8 Review Tab
                    const tabCases = new bootstrap.Tab($('#tab-cases')[0]);
                    tabCases.show();
                } else {
                    Swal.fire('Scan Failed', res.message || 'Unknown error occurred while analyzing projects.', 'error');
                }
            },
            error: function (xhr) {
                Swal.close();
                Swal.fire('Error', 'Failed to connect to server: ' + (xhr.responseJSON?.message || xhr.statusText), 'error');
            }
        });
    } catch (err) {
        Swal.close();
        Swal.fire('Packaging Error', err.message || 'Error packaging source files.', 'error');
    }
}

/**
 * =========================================================================
 * Single Project Analysis (Legacy / Component Mode with JSZip fix)
 * =========================================================================
 */
async function analyzeTargetProject() {
    const formData = new FormData();
    const folderFiles = getActiveFolderFiles();
    const zipFile = $('#projectZipInput')[0]?.files[0];
    const csFiles = $('#csharpFilesInput')[0]?.files;
    const path = $('#targetPathInput').val().trim();

    Swal.fire({
        title: 'Inspecting C# Architecture...',
        html: '<div class="spinner-border text-primary" role="status"></div><div class="mt-2 text-muted small">Roslyn AST is parsing classes, controllers, and dependency injections...</div>',
        allowOutsideClick: false,
        showConfirmButton: false
    });

    try {
        if (folderFiles && folderFiles.length > 0) {
            // Compress with JSZip to bypass 1024 form key limit and upload instantly
            if (typeof JSZip !== 'undefined') {
                const packaged = await packageFolderFilesWithJSZip(folderFiles);
                if (!packaged || packaged.count === 0) {
                    Swal.fire('No C# Files Found', 'No .cs or .csproj files found in the uploaded folder (ignoring bin/obj).', 'warning');
                    return;
                }
                formData.append('projectZip', packaged.blob, 'ProjectSource.zip');
            } else {
                appendFolderFilesToFormData(formData);
            }
        } else if (zipFile) {
            formData.append('projectZip', zipFile);
        } else if (csFiles && csFiles.length > 0) {
            for (let i = 0; i < csFiles.length; i++) {
                formData.append('csharpFiles', csFiles[i]);
            }
        } else if (path) {
            formData.append('targetPath', path);
        } else {
            Swal.fire('Input Required', 'Please enter a target project/file path or upload a complete folder or archive.', 'warning');
            return;
        }

        $.ajax({
            url: '/UnitTestGenerator/AnalyzeProject',
            type: 'POST',
            data: formData,
            contentType: false,
            processData: false,
            success: function (res) {
                Swal.close();
                if (res.success && res.result) {
                    const data = res.result;
                    currentProjectAnalysis = data;
                    allDiscoveredClasses = data.classes || [];
                    currentReviewCases = res.reviewTestCases || [];

                    $('#projectDiscoverySection').removeClass('d-none');
                    $('#detectedRoleBadge').text(data.projectName);

                    renderClassOptions(allDiscoveredClasses);
                    renderReviewTestCasesTable(currentReviewCases);

                    // Update Dashboard
                    $('#dashProjectsCount').text(1);
                    $('#dashProjectsLabel').text(data.projectName || 'Single Project');
                    $('#dashClassesCount').text(data.totalClasses);
                    $('#dashSourceFilesLabel').text(`${data.totalSourceFiles} Files`);
                    $('#dashMethodsCount').text(data.totalMethods);
                    $('#dashTestCasesCount').text(currentReviewCases.length);
                    $('#dashApprovedBadge').text(`${currentReviewCases.filter(c => c.isApproved).length} Approved`);

                    updateDashboardCharts(currentReviewCases);

                    Swal.fire({
                        icon: 'success',
                        title: 'Project Scanned Successfully!',
                        html: `Detected <strong>${data.totalClasses}</strong> classes across <strong>${data.totalSourceFiles}</strong> source files. Synthesized <strong>${currentReviewCases.length}</strong> test cases for review.`,
                        timer: 2000,
                        showConfirmButton: false
                    });

                    const tabCases = new bootstrap.Tab($('#tab-cases')[0]);
                    tabCases.show();
                } else {
                    Swal.fire('Scan Failed', res.message || 'Unknown error occurred while analyzing the target project.', 'error');
                }
            },
            error: function (xhr) {
                Swal.close();
                Swal.fire('Scan Failed', xhr.responseJSON?.message || xhr.statusText, 'error');
            }
        });
    } catch (err) {
        Swal.close();
        Swal.fire('Error', err.message || 'Failed packaging folder.', 'error');
    }
}

/**
 * =========================================================================
 * Step 8: Render Review Test Cases Table (Interactive DataTable)
 * =========================================================================
 */
function renderReviewTestCasesTable(cases) {
    const tbody = $('#testCasesTableBody');
    tbody.empty();

    if (!cases || cases.length === 0) {
        tbody.html('<tr><td colspan="10" class="text-center text-muted py-4">No test cases generated. Scan a project to synthesize scenarios.</td></tr>');
        $('#testCasesCountBadge').text('0');
        $('#approvedCasesBadge').text('0 Approved');
        return;
    }

    $('#testCasesCountBadge').text(cases.length);
    const approvedCount = cases.filter(c => c.isApproved).length;
    $('#approvedCasesBadge').text(`${approvedCount} Approved`);
    $('#approvedCountBadge').text(approvedCount);

    cases.forEach((item, index) => {
        const tr = $('<tr>').attr('id', `row-${item.id}`);

        // Category Badge
        let catBadgeClass = 'bg-secondary';
        if (item.category === 'HappyPath') catBadgeClass = 'bg-primary';
        else if (item.category === 'NullValidation') catBadgeClass = 'bg-warning text-dark';
        else if (item.category === 'Boundary') catBadgeClass = 'bg-info text-dark';
        else if (item.category === 'BRD') catBadgeClass = 'bg-success';
        else if (item.category === 'CustomUser') catBadgeClass = 'bg-purple text-white';
        else if (item.category === 'ExistingCovered') catBadgeClass = 'bg-dark text-light';

        // Project Role Badge
        const isWeb = item.targetProjectRole?.includes('Web');
        const projBadge = isWeb 
            ? '<span class="badge bg-primary-subtle text-primary border border-primary-subtle">Web Client</span>' 
            : '<span class="badge bg-success-subtle text-success border border-success-subtle">Core API</span>';

        // Status Badge
        let statusBadge = '<span class="badge bg-light text-secondary border">Suggested</span>';
        if (item.isExisting) statusBadge = '<span class="badge bg-info text-dark"><i class="ri-check-line me-1"></i>Existing</span>';
        else if (item.status === 'Approved') statusBadge = '<span class="badge bg-success"><i class="ri-check-double-line me-1"></i>Approved</span>';
        else if (item.status === 'Updated') statusBadge = '<span class="badge bg-warning text-dark"><i class="ri-loop-right-line me-1"></i>Needs Update</span>';
        else if (item.status === 'Rejected') statusBadge = '<span class="badge bg-danger">Rejected</span>';

        tr.html(`
            <td class="text-center">
                <input type="checkbox" class="case-select-checkbox form-check-input" data-case-id="${item.id}" ${item.isApproved ? 'checked' : ''} onchange="toggleCaseApproval('${item.id}', this.checked)" />
            </td>
            <td><span class="font-monospace small fw-bold text-dark">${item.id}</span></td>
            <td>${projBadge}</td>
            <td>
                <div class="fw-bold text-dark">${escapeHtml(item.targetClass)}</div>
                <div class="font-monospace small text-primary">${escapeHtml(item.targetMethod)}</div>
            </td>
            <td><span class="badge ${catBadgeClass} rounded-pill px-2 py-1">${item.category}</span></td>
            <td><span class="small font-monospace text-secondary">${escapeHtml(item.testInput || '-')}</span></td>
            <td><span class="small fw-semibold text-dark">${escapeHtml(item.expectedResult || '-')}</span></td>
            <td><span class="badge bg-light text-secondary border small">${escapeHtml(item.source)}</span></td>
            <td id="status-${item.id}">${statusBadge}</td>
            <td class="text-center">
                <button type="button" class="btn btn-outline-secondary btn-xs py-0 px-2 rounded-pill" onclick="openEditCaseModal('${item.id}')" title="Edit Scenario">
                    <i class="ri-edit-line"></i>
                </button>
            </td>
        `);

        tbody.append(tr);
    });
}

function filterAndRenderReviewCases() {
    const query = ($('#testCaseSearchInput').val() || '').trim().toLowerCase();
    const selectedClass = $('#classSelectorDropdown').val() || 'ALL';
    let filtered = currentReviewCases || [];

    // Filter by selected target class (if a custom class is selected)
    if (selectedClass && selectedClass !== 'ALL') {
        const prefix = selectedClass.replace('Controller', '').replace('BaseController', '').replace('Service', '');
        filtered = filtered.filter(c => {
            if (!c.targetClass) return false;
            if (c.targetClass.toLowerCase() === selectedClass.toLowerCase()) return true;
            // Also include paired Web/API controller if applicable (e.g. WorkEvidenceController & WorkEvidenceBaseController)
            const cPrefix = c.targetClass.replace('Controller', '').replace('BaseController', '').replace('Service', '');
            return (prefix.length > 3 && cPrefix.length > 3 && prefix.toLowerCase() === cPrefix.toLowerCase());
        });
    }

    // Filter by project role
    if (activeProjectFilter === 'web') {
        filtered = filtered.filter(c => c.targetProjectRole?.toLowerCase().includes('web'));
    } else if (activeProjectFilter === 'api') {
        filtered = filtered.filter(c => c.targetProjectRole?.toLowerCase().includes('api') || c.targetProjectRole?.toLowerCase().includes('service'));
    }

    // Filter by category
    if (activeCaseFilter !== 'all') {
        filtered = filtered.filter(c => c.category === activeCaseFilter);
    }

    // Filter by text search
    if (query) {
        filtered = filtered.filter(c => 
            c.id?.toLowerCase().includes(query) ||
            c.testName?.toLowerCase().includes(query) ||
            c.targetClass?.toLowerCase().includes(query) ||
            c.targetMethod?.toLowerCase().includes(query) ||
            c.expectedResult?.toLowerCase().includes(query)
        );
    }

    renderReviewTestCasesTable(filtered);
}

function toggleCaseApproval(caseId, isApproved) {
    const item = currentReviewCases.find(c => c.id === caseId);
    if (item) {
        item.isApproved = isApproved;
        item.status = isApproved ? 'Approved' : 'Rejected';
        const badgeElem = $(`#status-${caseId}`);
        if (badgeElem.length) {
            badgeElem.html(isApproved 
                ? '<span class="badge bg-success"><i class="ri-check-double-line me-1"></i>Approved</span>'
                : '<span class="badge bg-danger">Rejected</span>');
        }
        const approvedCount = currentReviewCases.filter(c => c.isApproved).length;
        $('#approvedCasesBadge').text(`${approvedCount} Approved`);
        $('#approvedCountBadge').text(approvedCount);
    }
}

function toggleSelectAllCases(checkbox) {
    const isChecked = $(checkbox).is(':checked');
    selectAllReviewCases(isChecked);
}

function selectAllReviewCases(select) {
    const selectedClass = $('#classSelectorDropdown').val() || 'ALL';
    const prefix = (selectedClass && selectedClass !== 'ALL') ? selectedClass.replace('Controller', '').replace('BaseController', '').replace('Service', '') : '';

    currentReviewCases.forEach(c => {
        let matches = true;
        if (selectedClass && selectedClass !== 'ALL') {
            const cPrefix = (c.targetClass || '').replace('Controller', '').replace('BaseController', '').replace('Service', '');
            matches = (c.targetClass && c.targetClass.toLowerCase() === selectedClass.toLowerCase()) || 
                      (prefix.length > 3 && cPrefix.length > 3 && prefix.toLowerCase() === cPrefix.toLowerCase());
        }
        if (matches) {
            c.isApproved = select;
            c.status = select ? 'Approved' : 'Rejected';
        }
    });
    filterAndRenderReviewCases();
}

function approveSelectedReviewCases(approve) {
    $('.case-select-checkbox:checked').each(function () {
        const id = $(this).data('case-id');
        const item = currentReviewCases.find(c => c.id === id);
        if (item) {
            item.isApproved = approve;
            item.status = approve ? 'Approved' : 'Rejected';
        }
    });
    filterAndRenderReviewCases();
}

/**
 * =========================================================================
 * Step 9: Generate Unit Tests from Approved Cases
 * =========================================================================
 */
function generateUnitTestsFromApprovedCases() {
    const selectedClass = $('#classSelectorDropdown').val() || 'ALL';
    let approvedCases = currentReviewCases.filter(c => c.isApproved);

    if (selectedClass && selectedClass !== 'ALL') {
        const prefix = selectedClass.replace('Controller', '').replace('BaseController', '').replace('Service', '');
        approvedCases = approvedCases.filter(c => {
            if (c.targetClass === selectedClass) return true;
            const cPrefix = (c.targetClass || '').replace('Controller', '').replace('BaseController', '').replace('Service', '');
            return (prefix.length > 3 && cPrefix.length > 3 && prefix.toLowerCase() === cPrefix.toLowerCase());
        });
    }

    if (approvedCases.length === 0) {
        Swal.fire('No Approved Test Cases', selectedClass !== 'ALL' 
            ? `Please approve at least one test case for ${selectedClass} before generating.` 
            : 'Please select and approve at least one test case in the table before generating C# unit test code.', 'warning');
        return;
    }

    const targetClass = selectedClass !== 'ALL' ? selectedClass : (approvedCases[0]?.targetClass || 'PurchaseOrderBaseController');
    const existingTestPath = $('#existingTestPathInput').val().trim();
    let targetPath = '';
    const classObj = (allDiscoveredClasses || []).find(c => c.className === targetClass);
    if (classObj && classObj.filePath) {
        targetPath = classObj.filePath;
    } else {
        const isWeb = classObj ? classObj.isWebController : (targetClass.includes('Web') || (!targetClass.includes('BaseController') && !targetClass.includes('Api')));
        targetPath = isWeb
            ? ($('#webProjectPathInput').val().trim() || $('#targetPathInput').val().trim() || $('#apiProjectPathInput').val().trim())
            : ($('#apiProjectPathInput').val().trim() || $('#targetPathInput').val().trim() || $('#webProjectPathInput').val().trim());
    }

    Swal.fire({
        title: 'Generating C# Test Suite...',
        html: `<div class="spinner-border text-primary" role="status"></div><div class="mt-2 text-muted small">Generating clean AAA xUnit + Moq tests for ${approvedCases.length} approved scenarios...</div>`,
        allowOutsideClick: false,
        showConfirmButton: false
    });

    $.ajax({
        url: '/UnitTestGenerator/GenerateTestsFromReviewCases',
        type: 'POST',
        data: {
            targetClassName: targetClass,
            targetPath: targetPath,
            existingTestFilePath: existingTestPath,
            isAugmentExisting: false,
            reviewCasesJson: JSON.stringify(approvedCases)
        },
        success: function (res) {
            Swal.close();
            if (res.success) {
                currentGeneratedCode = res.testCode;
                currentTargetClassName = res.className;

                // Render in code viewer
                renderCodeInViewer(currentGeneratedCode, res.className);

                // Update Badges
                $('#activeClassNameBadge').text(`${res.className}Tests`);
                $('#activeRoleBadge').text('Synthesized Test Suite');

                // Render Diagnostics
                renderDiagnostics(res.diagnostics || [], (res.diagnostics || []).some(d => d.severity === 'Error'));

                // Switch to Code Tab
                const tabCode = new bootstrap.Tab($('#tab-code')[0]);
                tabCode.show();

                Swal.fire({
                    icon: 'success',
                    title: 'Unit Tests Generated!',
                    html: `Compiled <strong>${approvedCases.length}</strong> approved test scenarios into <code>${res.className}Tests.cs</code> with 0 syntax errors.`,
                    timer: 2000,
                    showConfirmButton: false
                });
            } else {
                Swal.fire('Generation Error', res.message || 'Failed generating unit test code.', 'error');
            }
        },
        error: function (xhr) {
            Swal.close();
            Swal.fire('Error', xhr.responseJSON?.message || xhr.statusText, 'error');
        }
    });
}

/**
 * =========================================================================
 * Update Existing Test Suite (Augment Missing Tests)
 * =========================================================================
 */
function updateExistingTestSuiteAction() {
    const existingTestPath = $('#existingTestPathInput').val().trim();
    if (!existingTestPath) {
        Swal.fire('Path Required', 'Please specify an existing test project path or file to update.', 'warning');
        return;
    }

    const missingCases = currentReviewCases.filter(c => c.isApproved && !c.isExisting);
    if (missingCases.length === 0) {
        Swal.fire('All Covered', 'All identified scenarios are already covered in the existing test suite! No missing gaps detected.', 'info');
        return;
    }

    Swal.fire({
        title: 'Update Existing Test Suite?',
        html: `Found <strong>${missingCases.length}</strong> new / missing test scenarios that are not yet in your existing test suite.<br/><br/>EnterpriseTestGen will append these tests while safely preserving all your existing test implementations and constructor setups.`,
        icon: 'question',
        showCancelButton: true,
        confirmButtonText: 'Yes, Augment Test Suite',
        cancelButtonText: 'Cancel'
    }).then(result => {
        if (result.isConfirmed) {
            const selectedClass = $('#classSelectorDropdown').val() || 'ALL';
            const targetClass = selectedClass !== 'ALL' ? selectedClass : (missingCases[0]?.targetClass || 'PurchaseOrderBaseController');

            Swal.fire({
                title: 'Augmenting Existing Test Class...',
                html: '<div class="spinner-border text-warning" role="status"></div><div class="mt-2 text-muted small">Injecting missing branch tests into existing test file...</div>',
                allowOutsideClick: false,
                showConfirmButton: false
            });

            $.ajax({
                url: '/UnitTestGenerator/GenerateTestsFromReviewCases',
                type: 'POST',
                data: {
                    targetClassName: targetClass,
                    targetPath: (allDiscoveredClasses || []).find(c => c.className === targetClass)?.filePath 
                                || ($('#webProjectPathInput').val().trim() || $('#apiProjectPathInput').val().trim()),
                    existingTestFilePath: existingTestPath,
                    isAugmentExisting: true,
                    reviewCasesJson: JSON.stringify(missingCases)
                },
                success: function (res) {
                    Swal.close();
                    if (res.success) {
                        currentGeneratedCode = res.testCode;
                        currentTargetClassName = res.className;

                        renderCodeInViewer(currentGeneratedCode, res.className);
                        $('#activeClassNameBadge').text(`${res.className}Tests (Augmented)`);

                        renderDiagnostics(res.diagnostics || [], (res.diagnostics || []).some(d => d.severity === 'Error'));

                        const tabCode = new bootstrap.Tab($('#tab-code')[0]);
                        tabCode.show();

                        Swal.fire({
                            icon: 'success',
                            title: 'Existing Test Suite Updated!',
                            html: `Successfully augmented with <strong>${missingCases.length}</strong> new edge cases and assertions! Click <strong>'Save'</strong> to commit directly to disk.`,
                            timer: 3000,
                            showConfirmButton: false
                        });
                    } else {
                        Swal.fire('Error', res.message, 'error');
                    }
                },
                error: function (xhr) {
                    Swal.close();
                    Swal.fire('Error', xhr.responseJSON?.message || xhr.statusText, 'error');
                }
            });
        }
    });
}

/**
 * =========================================================================
 * Render Web-to-API Communication Mappings Table
 * =========================================================================
 */
function renderWebToApiMappingsTable(mappings) {
    const tbody = $('#webToApiTableBody');
    tbody.empty();

    if (!mappings || mappings.length === 0) {
        tbody.html('<tr><td colspan="5" class="text-center text-muted py-4">No direct Web-to-API mappings identified.</td></tr>');
        $('#mappingsCountBadge').text('0');
        return;
    }

    $('#mappingsCountBadge').text(mappings.length);

    mappings.forEach(m => {
        const tr = $('<tr>');
        tr.html(`
            <td>
                <span class="fw-bold text-dark">${escapeHtml(m.webControllerName)}</span>
                <span class="text-primary font-monospace small d-block">.${escapeHtml(m.webActionMethod)}()</span>
            </td>
            <td><span class="badge ${m.httpMethod === 'GET' ? 'bg-info' : 'bg-primary'}">${m.httpMethod}</span></td>
            <td><code class="text-success small">${escapeHtml(m.apiEndpointUrl)}</code></td>
            <td>
                <span class="fw-semibold text-dark">${escapeHtml(m.targetApiController)}</span>
                <span class="text-secondary font-monospace small d-block">.${escapeHtml(m.targetApiMethod)}()</span>
            </td>
            <td><span class="badge bg-light text-secondary border">${escapeHtml(m.communicationPattern)}</span></td>
        `);
        tbody.append(tr);
    });
}

/**
 * =========================================================================
 * Custom Test Case Modal (Step 6)
 * =========================================================================
 */
function openAddCustomCaseModal() {
    $('#editTestCaseId').val('');
    $('#modalCaseName').val('');
    $('#modalCaseInputs').val('');
    $('#modalCaseExpected').val('');
    $('#customTestCaseModalLabel').text('Add Custom Test Case');

    const selectedClass = $('#classSelectorDropdown').val();
    if (selectedClass && selectedClass !== 'ALL') {
        $('#modalCaseTargetClass').val(selectedClass);
    } else if (allDiscoveredClasses.length > 0) {
        $('#modalCaseTargetClass').val(allDiscoveredClasses[0].className);
        $('#modalCaseTargetMethod').val(allDiscoveredClasses[0].methods[0]?.methodName || 'ExecuteAsync');
    }

    const modal = new bootstrap.Modal(document.getElementById('customTestCaseModal'));
    modal.show();
}

function openEditCaseModal(caseId) {
    const item = currentReviewCases.find(c => c.id === caseId);
    if (!item) return;

    $('#editTestCaseId').val(item.id);
    $('#modalCaseName').val(item.testName);
    $('#modalCaseCategory').val(item.category || 'HappyPath');
    $('#modalCaseProjectRole').val(item.targetProjectRole?.includes('Web') ? 'Web MVC' : 'Core API');
    $('#modalCasePriority').val(item.priority || 'Medium');
    $('#modalCaseTargetClass').val(item.targetClass);
    $('#modalCaseTargetMethod').val(item.targetMethod);
    $('#modalCaseInputs').val(item.testInput);
    $('#modalCaseExpected').val(item.expectedResult);
    $('#customTestCaseModalLabel').text(`Edit Test Case: ${item.id}`);

    const modal = new bootstrap.Modal(document.getElementById('customTestCaseModal'));
    modal.show();
}

function saveCustomTestCaseFromModal() {
    const id = $('#editTestCaseId').val().trim();
    const name = $('#modalCaseName').val().trim();
    const category = $('#modalCaseCategory').val();
    const role = $('#modalCaseProjectRole').val();
    const priority = $('#modalCasePriority').val();
    const targetClass = $('#modalCaseTargetClass').val().trim();
    const targetMethod = $('#modalCaseTargetMethod').val().trim();
    const inputs = $('#modalCaseInputs').val().trim();
    const expected = $('#modalCaseExpected').val().trim();

    if (!name || !targetClass || !targetMethod || !expected) {
        Swal.fire('Required Fields', 'Please enter Test Name, Target Class, Target Method, and Expected Result.', 'warning');
        return;
    }

    if (id) {
        // Edit existing
        const item = currentReviewCases.find(c => c.id === id);
        if (item) {
            item.testName = name;
            item.category = category;
            item.targetProjectRole = role;
            item.priority = priority;
            item.targetClass = targetClass;
            item.targetMethod = targetMethod;
            item.testInput = inputs;
            item.expectedResult = expected;
            item.status = 'Approved';
            item.isApproved = true;
        }
    } else {
        // Add new
        const newId = `TC-USR-${(currentReviewCases.length + 1).toString().padStart(3, '0')}`;
        currentReviewCases.unshift({
            id: newId,
            testName: name,
            targetProjectRole: role,
            projectName: role.includes('Web') ? 'WebClient' : 'CoreServer',
            targetClass: targetClass,
            targetMethod: targetMethod,
            category: category,
            testInput: inputs,
            expectedResult: expected,
            source: 'Custom User Case',
            priority: priority,
            status: 'Approved',
            isApproved: true,
            isExisting: false
        });
    }

    const modalEl = document.getElementById('customTestCaseModal');
    const modal = bootstrap.Modal.getInstance(modalEl);
    if (modal) modal.hide();

    filterAndRenderReviewCases();

    Swal.fire({
        icon: 'success',
        title: id ? 'Test Case Updated' : 'Custom Test Case Added',
        timer: 1500,
        showConfirmButton: false
    });
}

/**
 * =========================================================================
 * Dashboard Visual Charts (Chart.js)
 * =========================================================================
 */
function initDashboardCharts() {
    const ctx1 = document.getElementById('chartScenarioDistribution')?.getContext('2d');
    if (ctx1) {
        scenarioChartInstance = new Chart(ctx1, {
            type: 'doughnut',
            data: {
                labels: ['Happy Path', 'Null Guard', 'Boundary', 'BRD Rules', 'Custom', 'Existing Tests'],
                datasets: [{
                    data: [0, 0, 0, 0, 0, 0],
                    backgroundColor: ['#0d6efd', '#ffc107', '#0dcaf0', '#198754', '#6f42c1', '#212529']
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: { legend: { position: 'right', labels: { boxWidth: 12, font: { size: 10 } } } }
            }
        });
    }

    const ctx2 = document.getElementById('chartCoverageHealth')?.getContext('2d');
    if (ctx2) {
        coverageChartInstance = new Chart(ctx2, {
            type: 'bar',
            data: {
                labels: ['Line Cov', 'Branch Cov', 'Tests Passed', 'Tests Failed'],
                datasets: [{
                    label: 'Status % / Count',
                    data: [0, 0, 0, 0],
                    backgroundColor: ['#0d6efd', '#0dcaf0', '#198754', '#dc3545']
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: { legend: { display: false } },
                scales: { y: { beginAtZero: true, max: 100 } }
            }
        });
    }
}

function updateDashboardCharts(cases) {
    if (!scenarioChartInstance || !cases || cases.length === 0) return;

    const happy = cases.filter(c => c.category === 'HappyPath').length;
    const nullGuard = cases.filter(c => c.category === 'NullValidation').length;
    const boundary = cases.filter(c => c.category === 'Boundary').length;
    const brd = cases.filter(c => c.category === 'BRD').length;
    const custom = cases.filter(c => c.category === 'CustomUser').length;
    const existing = cases.filter(c => c.category === 'ExistingCovered').length;

    scenarioChartInstance.data.datasets[0].data = [happy, nullGuard, boundary, brd, custom, existing];
    scenarioChartInstance.update();
}

/**
 * Browse helper
 */
function browsePathFor(targetInputId) {
    currentBrowseTargetInputId = targetInputId;
    const currentVal = $(`#${targetInputId}`).val().trim();
    openDirectoryBrowserModal(currentVal);
}

/**
 * =========================================================================
 * Core UI Rendering & Helpers
 * =========================================================================
 */
function renderClassOptions(classes) {
    const dropdown = $('#classSelectorDropdown');
    dropdown.empty();
    dropdown.append('<option value="ALL">📦 ALL CLASSES (Batch Suite Generation)</option>');

    if (!classes || classes.length === 0) return;

    classes.forEach(c => {
        const option = $('<option>').val(c.className).text(`${c.className} (${c.projectRole || 'Class'}) [${c.methods.length} methods]`);
        dropdown.append(option);
    });

    $('#discoveryStatsBadge').text(`${classes.length} Classes`);
}

function updateSelectedClassRoleMetadata(className) {
    if (!className || className === 'ALL') {
        $('#selectedClassRoleText').text('Batch generation will compile tests for all analyzed classes.');
        return;
    }

    const cls = allDiscoveredClasses.find(c => c.className === className);
    if (!cls) return;

    $('#selectedClassRoleText').html(`<strong>Role:</strong> ${cls.projectRole || 'Class'} &bull; <strong>Namespace:</strong> ${cls.namespace} &bull; <strong>Dependencies:</strong> ${cls.constructorDependencies.length} mocks &bull; <strong>Methods:</strong> ${cls.methods.length}`);
}

function filterClassList() {
    const query = ($('#classSearchInput').val() || '').trim().toLowerCase();
    const dropdown = $('#classSelectorDropdown');
    dropdown.empty();
    dropdown.append('<option value="ALL">📦 ALL CLASSES (Batch Suite Generation)</option>');

    allDiscoveredClasses.forEach(c => {
        if (activeClassFilter === 'controllers' && !c.isWebController && !c.isApiController && !c.className.endsWith('Controller')) return;
        if (activeClassFilter === 'services' && !c.isService && !c.className.endsWith('Service')) return;
        if (activeClassFilter === 'methods-only' && c.methods.length === 0) return;

        if (query && !c.className.toLowerCase().includes(query) && !c.namespace.toLowerCase().includes(query)) return;

        const option = $('<option>').val(c.className).text(`${c.className} (${c.projectRole || 'Class'}) [${c.methods.length} methods]`);
        dropdown.append(option);
    });
}

function renderCodeInViewer(code, className) {
    const pre = $('#generatedCodeDisplay');
    pre.text(code);
    if (window.hljs) hljs.highlightElement(pre[0]);

    const lines = code.split('\n').length;
    $('#codeLineCount').text(`${lines} lines`);

    const gutter = $('#codeGutterNumbers');
    gutter.empty();
    for (let i = 1; i <= lines; i++) {
        gutter.append(`<div>${i}</div>`);
    }
}

function renderDiagnostics(diagnostics, hasErrors) {
    const badgeSuccess = $('#compilationSuccessBadge');
    const badgeError = $('#compilationErrorBadge');
    const alertBox = $('#diagnosticsErrorAlert');
    const errList = $('#diagnosticsErrorList');
    const diagBadge = $('#diagnosticsCountBadge');

    diagBadge.text(diagnostics ? diagnostics.length : 0);

    if (hasErrors || (diagnostics && diagnostics.some(d => d.severity === 'Error'))) {
        badgeSuccess.addClass('d-none');
        badgeError.removeClass('d-none');
        alertBox.removeClass('d-none');
        errList.empty();

        const errors = diagnostics.filter(d => d.severity === 'Error');
        errors.forEach(e => {
            errList.append(`<li>Line ${e.lineNumber}: [${e.errorId}] ${escapeHtml(e.message)} <code>${escapeHtml(e.codeSnippet || '')}</code></li>`);
        });

        $('#diagnosticsStatusIcon').attr('class', 'ri-bug-fill text-danger fs-3');
        $('#diagnosticsStatusBadge').attr('class', 'badge bg-danger px-3 py-2 fw-semibold').text(`${errors.length} Errors`);
    } else {
        badgeError.addClass('d-none');
        badgeSuccess.removeClass('d-none');
        alertBox.addClass('d-none');

        $('#diagnosticsStatusIcon').attr('class', 'ri-shield-check-line text-success fs-3');
        $('#diagnosticsStatusBadge').attr('class', 'badge bg-success px-3 py-2 fw-semibold').text('0 Errors');
    }
}

function copyGeneratedCodeToClipboard() {
    if (!currentGeneratedCode) {
        Swal.fire('No Code', 'Please generate unit tests first.', 'info');
        return;
    }
    navigator.clipboard.writeText(currentGeneratedCode).then(() => {
        Swal.fire({
            icon: 'success',
            title: 'Copied to Clipboard!',
            text: 'C# test class code ready to paste.',
            timer: 1500,
            showConfirmButton: false
        });
    });
}

function downloadSingleGeneratedCsFile() {
    if (!currentGeneratedCode) {
        Swal.fire('No Code', 'Please generate unit tests first.', 'info');
        return;
    }
    const filename = `${currentTargetClassName || 'Generated'}Tests.cs`;
    downloadBlob(currentGeneratedCode, filename, 'text/plain;charset=utf-8');
}

function saveGeneratedCodeToDisk() {
    if (!currentGeneratedCode) {
        Swal.fire('No Code', 'Please generate unit tests first.', 'info');
        return;
    }

    const defaultPath = $('#existingTestPathInput').val().trim() || 
        (currentTargetClassName ? `${currentTargetClassName}Tests.cs` : '');

    Swal.fire({
        title: 'Save Unit Test File to Disk',
        input: 'text',
        inputValue: defaultPath,
        inputLabel: 'Target File Path (.cs):',
        showCancelButton: true,
        confirmButtonText: 'Save File',
        inputValidator: value => {
            if (!value) return 'File path is required!';
        }
    }).then(result => {
        if (result.isConfirmed && result.value) {
            $.ajax({
                url: '/UnitTestGenerator/SaveAugmentedCodeToFile',
                type: 'POST',
                data: { filePath: result.value, updatedCode: currentGeneratedCode },
                success: function (res) {
                    if (res.success) {
                        Swal.fire('Saved!', res.message, 'success');
                    } else {
                        Swal.fire('Error Saving', res.message, 'error');
                    }
                }
            });
        }
    });
}

function downloadCompleteProjectTestsZip() {
    const formData = new FormData();
    const folderFiles = getActiveFolderFiles();
    const zipFile = $('#projectZipInput')[0]?.files[0];
    const csFiles = $('#csharpFilesInput')[0]?.files;
    const selectedClass = $('#classSelectorDropdown').val() || 'ALL';
    const targetClass = selectedClass !== 'ALL' ? selectedClass : (currentTargetClassName || 'Generated');
    const path = $('#apiProjectPathInput').val().trim() || $('#webProjectPathInput').val().trim() || $('#targetPathInput').val().trim();

    if (currentGeneratedCode) {
        formData.append('currentTestCode', currentGeneratedCode);
        formData.append('currentClassName', targetClass);
    }
    formData.append('selectedClassName', selectedClass);
    if (path) formData.append('targetPath', path);

    Swal.fire({
        title: 'Packaging Runnable Test Project...',
        html: '<div class="spinner-border text-primary" role="status"></div><div class="mt-2 text-muted small">Generating test classes, GeneratedTests.csproj & README...</div>',
        allowOutsideClick: false,
        showConfirmButton: false
    });

    const xhr = new XMLHttpRequest();
    xhr.open('POST', '/UnitTestGenerator/DownloadAllTestsZip', true);
    xhr.responseType = 'blob';

    xhr.onload = function () {
        Swal.close();
        if (this.status === 200) {
            const blob = new Blob([this.response], { type: 'application/zip' });
            const link = document.createElement('a');
            link.href = window.URL.createObjectURL(blob);
            link.download = (currentTargetClassName || 'Generated') + '_TestProject.zip';
            link.click();
            Swal.fire({
                icon: 'success',
                title: 'Download Ready',
                text: 'Turnkey xUnit test project downloaded (.ZIP). Unzip and run `dotnet test` directly!',
                timer: 2000,
                showConfirmButton: false
            });
        } else {
            Swal.fire('Error', 'Failed to generate ZIP project.', 'error');
        }
    };

    xhr.send(formData);
}

function executeDotnetTests() {
    const projectPath = $('#runnerProjectPathInput').val().trim();
    if (!projectPath) {
        Swal.fire('Path Required', 'Please enter the target test project path (.csproj) or test directory.', 'warning');
        return;
    }

    const selectedClass = $('#classSelectorDropdown').val() || 'ALL';
    const filterClass = selectedClass !== 'ALL' ? selectedClass : null;

    const consoleElem = $('#runnerConsoleOutput');
    const runBtn = $('#btnExecuteDotnetTest');
    runBtn.prop('disabled', true).html('<i class="ri-loader-4-line spin me-1"></i>Running...');
    consoleElem.html(`<span class="text-warning"><i class="ri-loader-4-line spin me-1"></i>Building and executing \`dotnet test\` ${filterClass ? '(Filtered to: ' + filterClass + ')' : '(Full Suite)'}...</span>`);

    $.ajax({
        url: '/UnitTestGenerator/RunTests',
        type: 'POST',
        data: { 
            testProjectPath: projectPath,
            filterClass: filterClass
        },
        timeout: 130000,
        success: function (res) {
            runBtn.prop('disabled', false).html('<i class="ri-play-fill me-1"></i>Execute dotnet test');
            if (res.success) {
                $('#statTestsPassed').text(res.passed);
                $('#statTestsFailed').text(res.failed);
                $('#statLineCoverage').text(`${(res.lineCoverage || 0).toFixed(1)}%`);
                $('#statBranchCoverage').text(`${(res.branchCoverage || 0).toFixed(1)}%`);

                $('#dashTestsPassed').text(res.passed);
                $('#dashTestsFailed').text(res.failed);
                $('#dashLineCov').text(`${(res.lineCoverage || 0).toFixed(1)}%`);
                $('#dashBranchCov').text(`${(res.branchCoverage || 0).toFixed(1)}%`);

                const logsText = (res.logs || []).join('\n');
                consoleElem.text(logsText || 'Tests completed with status: Success.');

                const tabRunner = new bootstrap.Tab($('#tab-runner')[0]);
                tabRunner.show();
            } else {
                consoleElem.html(`<span class="text-danger">Error: ${res.message}</span>`);
            }
        },
        error: function (xhr, status) {
            runBtn.prop('disabled', false).html('<i class="ri-play-fill me-1"></i>Execute dotnet test');
            if (status === 'timeout') {
                consoleElem.html('<span class="text-danger"><i class="ri-alarm-warning-line me-1"></i>Execution timed out after 120s. Check if dependencies or connection string are required.</span>');
            } else {
                consoleElem.html(`<span class="text-danger">Execution error: ${xhr.responseText || xhr.statusText}</span>`);
            }
        }
    });
}

function exportTestCasesToCsv() {
    if (!currentReviewCases || currentReviewCases.length === 0) {
        Swal.fire('No Data', 'No test cases to export.', 'info');
        return;
    }

    let csv = 'TestID,ProjectRole,TargetClass,TargetMethod,TestName,Category,Inputs,ExpectedResult,Source,Status\n';
    currentReviewCases.forEach(c => {
        csv += `"${c.id}","${c.targetProjectRole}","${c.targetClass}","${c.targetMethod}","${c.testName}","${c.category}","${(c.testInput || '').replace(/"/g, '""')}","${(c.expectedResult || '').replace(/"/g, '""')}","${c.source}","${c.status}"\n`;
    });

    downloadBlob(csv, `SynthesizedTestCases_${DateTimeFormat(new Date())}.csv`, 'text/csv;charset=utf-8;');
}

function DateTimeFormat(d) {
    return d.toISOString().replace(/[:.]/g, '-');
}

function downloadBlob(content, filename, contentType) {
    const blob = new Blob([content], { type: contentType });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
}

function resetForm() {
    selectedWebFolderFiles = null;
    selectedApiFolderFiles = null;
    selectedSingleFolderFiles = null;
    selectedExistingTestFolderFiles = null;
    $('#projectZipInput').val('');
    $('#csharpFilesInput').val('');
    $('#folderUploadInput').val('');
    $('#targetPathInput').val('');
    $('#webFolderInput').val('');
    $('#webZipInput').val('');
    $('#apiFolderInput').val('');
    $('#apiZipInput').val('');
    $('#existingTestPathInput').val('');
    $('#webProjectPathInput').val('');
    $('#apiProjectPathInput').val('');
    $('#existingTestFolderInput').val('');
    $('#fileUploadStatusNotice').addClass('d-none').empty();
    $('#webUploadNotice').empty();
    $('#apiUploadNotice').empty();
    $('#detectedRoleBadge').text('Ready to Scan');
    $('#projectDiscoverySection').addClass('d-none');
    $('#classSelectorDropdown').empty().append('<option value="ALL">📦 ALL CLASSES</option>');
    $('#generatedCodeDisplay').text('// Workspace reset. Please select a source and click Scan.');
    $('#dashProjectsCount').text(0);
    $('#dashProjectsLabel').text('0 Projects');
    $('#dashClassesCount').text(0);
    $('#dashSourceFilesLabel').text('0 Files');
    $('#dashMethodsCount').text(0);
    $('#dashTestCasesCount').text(0);
    $('#dashApprovedBadge').text('0 Approved');
    $('#dashTestsPassed').text(0);
    $('#dashTestsFailed').text(0);
    $('#dashLineCov').text('0%');
    $('#dashBranchCov').text('0%');
    $('#testCasesCountBadge').text('0');
    $('#mappingsCountBadge').text('0');
    $('#diagnosticsCountBadge').text('0');
    $('#approvedCasesBadge').text('0 Approved');
    if (scenarioChartInstance) {
        scenarioChartInstance.data.datasets[0].data = [0, 0, 0, 0, 0, 0];
        scenarioChartInstance.update();
    }
    if (coverageChartInstance) {
        coverageChartInstance.data.datasets[0].data = [0, 0, 0, 0];
        coverageChartInstance.update();
    }
    currentGeneratedCode = '';
    currentTargetClassName = '';
    currentProjectAnalysis = null;
    currentMultiProjectAnalysis = null;
    allDiscoveredClasses = [];
    currentReviewCases = [];
    renderReviewTestCasesTable([]);
}

function escapeHtml(text) {
    if (!text) return '';
    return text.toString()
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#039;');
}

function escapePath(path) {
    return path.replace(/\\/g, '\\\\').replace(/'/g, "\\'");
}

/**
 * =========================================================================
 * Interactive Directory & Folder Browser Logic
 * =========================================================================
 */
let currentBrowserPath = '';
let currentBrowserParentPath = null;
let currentBrowserItems = [];

function openDirectoryBrowserModal(startPath) {
    const initial = (startPath || $(`#${currentBrowseTargetInputId}`).val() || '').trim();
    const modalEl = document.getElementById('folderBrowserModal');
    const modal = bootstrap.Modal.getOrCreateInstance(modalEl);
    modal.show();

    if (!initial) {
        currentBrowserPath = '';
        currentBrowserParentPath = null;
        currentBrowserItems = [];
        $('#browserCurrentPathText').text('No folder selected');
        $('#btnBrowserGoUp').prop('disabled', true).addClass('opacity-50');
        $('#browserFilterInput').val('');
        $('#btnSelectCurrentDirectory').prop('disabled', true).addClass('opacity-50');

        $('#browserItemsList').html(`
            <div class="text-center py-5 text-muted">
                <i class="ri-folder-open-line fs-1 text-secondary mb-2 d-block"></i>
                <div class="fw-semibold text-dark mb-1">No Folder Selected from System</div>
                <div class="small text-muted mb-3">Fetch a project folder from your system or enter a local path to inspect.</div>
                <button type="button" class="btn btn-primary btn-sm rounded-pill px-4 shadow-sm" onclick="$('#browserNativeFolderInput').click()">
                    <i class="ri-folder-upload-line me-1"></i>Fetch Folder from System
                </button>
            </div>
        `);
    } else {
        loadDirectoryPath(initial);
    }
}

function loadDirectoryPath(path) {
    if (!path) {
        currentBrowserPath = '';
        currentBrowserParentPath = null;
        currentBrowserItems = [];
        $('#browserCurrentPathText').text('No folder selected');
        $('#btnBrowserGoUp').prop('disabled', true).addClass('opacity-50');
        $('#browserFilterInput').val('');
        $('#btnSelectCurrentDirectory').prop('disabled', true).addClass('opacity-50');
        $('#browserItemsList').html(`
            <div class="text-center py-5 text-muted">
                <i class="ri-folder-open-line fs-1 text-secondary mb-2 d-block"></i>
                <div class="fw-semibold text-dark mb-1">No Folder Selected from System</div>
                <div class="small text-muted mb-3">Fetch a project folder from your system or enter a local path to inspect.</div>
                <button type="button" class="btn btn-primary btn-sm rounded-pill px-4 shadow-sm" onclick="$('#browserNativeFolderInput').click()">
                    <i class="ri-folder-upload-line me-1"></i>Fetch Folder from System
                </button>
            </div>
        `);
        return;
    }

    currentBrowserPath = path;
    $('#browserCurrentPathText').text(path);
    $('#browserFilterInput').val('');
    $('#btnSelectCurrentDirectory').prop('disabled', false).removeClass('opacity-50');

    const listContainer = $('#browserItemsList');
    listContainer.html(`
        <div class="text-center py-4 text-muted">
            <div class="spinner-border spinner-border-sm text-primary" role="status"></div>
            <div class="small mt-2">Loading directory contents...</div>
        </div>
    `);

    $.ajax({
        url: '/UnitTestGenerator/BrowseDirectories',
        type: 'GET',
        data: { path: path },
        success: function (res) {
            if (res.success) {
                currentBrowserPath = res.currentPath;
                currentBrowserParentPath = res.parentPath;
                $('#browserCurrentPathText').text(res.currentPath || 'No folder selected');

                if (res.parentPath) {
                    $('#btnBrowserGoUp').prop('disabled', false).removeClass('opacity-50');
                } else {
                    $('#btnBrowserGoUp').prop('disabled', true).addClass('opacity-50');
                }

                currentBrowserItems = (res.directories || []).concat(res.files || []);
                renderBrowserItems(currentBrowserItems);
            } else {
                listContainer.html(`<div class="alert alert-danger small p-2 m-2">Could not read directory: ${escapeHtml(res.message)}</div>`);
            }
        },
        error: function (xhr) {
            listContainer.html(`<div class="alert alert-danger small p-2 m-2">Error connecting to file system: ${escapeHtml(xhr.statusText)}</div>`);
        }
    });
}

function renderBrowserItems(items) {
    const listContainer = $('#browserItemsList');
    listContainer.empty();

    if (!items || items.length === 0) {
        listContainer.html('<div class="text-center text-muted py-4 small">Empty folder or no matching files found.</div>');
        return;
    }

    items.forEach(item => {
        const itemElem = $('<div>').addClass('list-group-item list-group-item-action d-flex align-items-center justify-content-between py-2 px-3 border-bottom');

        if (item.isDir) {
            let badge = '';
            if (item.isSolution) badge = '<span class="badge bg-warning text-dark me-2 small">Solution (.sln)</span>';
            else if (item.isProject) badge = '<span class="badge bg-primary text-white me-2 small">Project (.csproj)</span>';

            itemElem.html(`
                <div class="d-flex align-items-center gap-2 cursor-pointer flex-grow-1 text-truncate" onclick="loadDirectoryPath('${escapePath(item.fullPath)}')">
                    <i class="ri-folder-fill text-warning fs-5"></i>
                    <span class="fw-semibold text-dark text-truncate">${escapeHtml(item.name)}</span>
                    ${badge}
                </div>
                <div class="d-flex align-items-center gap-1">
                    <button type="button" class="btn btn-outline-primary btn-sm py-0 px-2 rounded-pill small" onclick="confirmDirectorySelection('${escapePath(item.fullPath)}')">
                        <i class="ri-check-line me-1"></i>Select
                    </button>
                </div>
            `);
        } else {
            let fileIcon = 'ri-file-code-line text-info';
            if (item.name.endsWith('.csproj')) fileIcon = 'ri-box-3-fill text-primary';
            if (item.name.endsWith('.sln')) fileIcon = 'ri-stack-fill text-warning';

            itemElem.html(`
                <div class="d-flex align-items-center gap-2 cursor-pointer flex-grow-1 text-truncate" onclick="confirmDirectorySelection('${escapePath(item.fullPath)}')">
                    <i class="${fileIcon} fs-5"></i>
                    <span class="text-secondary text-truncate">${escapeHtml(item.name)}</span>
                </div>
                <div>
                    <button type="button" class="btn btn-light border btn-sm py-0 px-2 rounded-pill small" onclick="confirmDirectorySelection('${escapePath(item.fullPath)}')">
                        <i class="ri-check-line me-1"></i>Choose
                    </button>
                </div>
            `);
        }

        listContainer.append(itemElem);
    });
}

function navigateParentDirectory() {
    if (currentBrowserParentPath) {
        loadDirectoryPath(currentBrowserParentPath);
    }
}

function filterDirectoryItems() {
    const q = $('#browserFilterInput').val().toLowerCase().trim();
    if (!q) {
        renderBrowserItems(currentBrowserItems);
        return;
    }
    const filtered = currentBrowserItems.filter(item => item.name.toLowerCase().includes(q));
    renderBrowserItems(filtered);
}

function confirmDirectorySelection(selectedPath) {
    const finalPath = selectedPath || currentBrowserPath;
    $(`#${currentBrowseTargetInputId}`).val(finalPath);

    const modalEl = document.getElementById('folderBrowserModal');
    const modal = bootstrap.Modal.getInstance(modalEl);
    if (modal) modal.hide();
}

function handleNativeFolderSelection(input) {
    const files = input.files;
    if (files && files.length > 0) {
        const rootFolder = files[0].webkitRelativePath ? files[0].webkitRelativePath.split('/')[0] : 'SelectedFolder';
        
        let csCount = 0;
        for (let i = 0; i < files.length; i++) {
            if ((files[i].webkitRelativePath || files[i].name).endsWith('.cs')) csCount++;
        }

        if (currentBrowseTargetInputId === 'webProjectPathInput') {
            selectedWebFolderFiles = files;
            $('#webProjectPathInput').val(rootFolder);
            $('#webUploadNotice').html(`<i class="ri-check-line text-success"></i> Folder: <strong>${escapeHtml(rootFolder)}</strong> (${files.length} files, ${csCount} .cs)`);
        } else if (currentBrowseTargetInputId === 'apiProjectPathInput') {
            selectedApiFolderFiles = files;
            $('#apiProjectPathInput').val(rootFolder);
            $('#apiUploadNotice').html(`<i class="ri-check-line text-success"></i> Folder: <strong>${escapeHtml(rootFolder)}</strong> (${files.length} files, ${csCount} .cs)`);
        } else if (currentBrowseTargetInputId === 'existingTestPathInput') {
            selectedExistingTestFolderFiles = files;
            $('#existingTestPathInput').val(rootFolder);
        } else {
            selectedSingleFolderFiles = files;
            $('#targetPathInput').val(rootFolder);
            $('#fileUploadStatusNotice').removeClass('d-none').html(`<i class="ri-folder-check-line text-success me-1"></i>Loaded local folder: <strong>${escapeHtml(rootFolder)}</strong> (${files.length} files). Click 'Scan Project' to analyze.`);
        }

        const modalEl = document.getElementById('folderBrowserModal');
        const modal = bootstrap.Modal.getInstance(modalEl);
        if (modal) modal.hide();

        Swal.fire({
            icon: 'success',
            title: 'Folder Fetched from System',
            html: `Selected: <strong>${escapeHtml(rootFolder)}</strong><br><small class="text-muted">${files.length} files (${csCount} C# source files)</small>`,
            timer: 2000,
            showConfirmButton: false
        });
    }
}
