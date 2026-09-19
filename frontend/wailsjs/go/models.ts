export namespace ai {
	
	export class AnalysisOutput {
	    engine: string;
	    modelId: string;
	    modelVersion: string;
	    resultJson: string;
	
	    static createFrom(source: any = {}) {
	        return new AnalysisOutput(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.engine = source["engine"];
	        this.modelId = source["modelId"];
	        this.modelVersion = source["modelVersion"];
	        this.resultJson = source["resultJson"];
	    }
	}
	export class InstalledModelInfo {
	    id: string;
	    version: string;
	    engine: string;
	    displayName: string;
	    license: string;
	    sizeBytes: number;
	    rootDir: string;
	
	    static createFrom(source: any = {}) {
	        return new InstalledModelInfo(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.id = source["id"];
	        this.version = source["version"];
	        this.engine = source["engine"];
	        this.displayName = source["displayName"];
	        this.license = source["license"];
	        this.sizeBytes = source["sizeBytes"];
	        this.rootDir = source["rootDir"];
	    }
	}
	export class JobQueue {
	
	
	    static createFrom(source: any = {}) {
	        return new JobQueue(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	
	    }
	}
	export class JobQueueStatus {
	    started: boolean;
	    workers: number;
	    activeCount: number;
	    activeCapabilities: string[];
	    pausedCapabilities: string[];
	
	    static createFrom(source: any = {}) {
	        return new JobQueueStatus(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.started = source["started"];
	        this.workers = source["workers"];
	        this.activeCount = source["activeCount"];
	        this.activeCapabilities = source["activeCapabilities"];
	        this.pausedCapabilities = source["pausedCapabilities"];
	    }
	}
	export class Manager {
	
	
	    static createFrom(source: any = {}) {
	        return new Manager(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	
	    }
	}
	export class RuntimeStatus {
	    capability: string;
	    state: string;
	    modelId?: string;
	    version?: string;
	    engine?: string;
	    executionProvider?: string;
	    warning?: string;
	    error?: string;
	
	    static createFrom(source: any = {}) {
	        return new RuntimeStatus(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.capability = source["capability"];
	        this.state = source["state"];
	        this.modelId = source["modelId"];
	        this.version = source["version"];
	        this.engine = source["engine"];
	        this.executionProvider = source["executionProvider"];
	        this.warning = source["warning"];
	        this.error = source["error"];
	    }
	}

}

export namespace commands {
	
	export class SemanticIndexStatus {
	    state: string;
	    loadedCount: number;
	    totalCount: number;
	    dimensions: number;
	    elapsedMs: number;
	    updatedAgoMs: number;
	    modelId?: string;
	    persistent: boolean;
	    overlayCount: number;
	    error?: string;
	
	    static createFrom(source: any = {}) {
	        return new SemanticIndexStatus(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.state = source["state"];
	        this.loadedCount = source["loadedCount"];
	        this.totalCount = source["totalCount"];
	        this.dimensions = source["dimensions"];
	        this.elapsedMs = source["elapsedMs"];
	        this.updatedAgoMs = source["updatedAgoMs"];
	        this.modelId = source["modelId"];
	        this.persistent = source["persistent"];
	        this.overlayCount = source["overlayCount"];
	        this.error = source["error"];
	    }
	}
	export class AIHealthSnapshot {
	    settings: domain.AISettings;
	    settingsPersisted: boolean;
	    // Go type: time
	    settingsUpdatedAt?: any;
	    semanticRuntime: ai.RuntimeStatus;
	    semanticIndex: SemanticIndexStatus;
	    queue: ai.JobQueueStatus;
	    shuttingDown: boolean;
	    semanticSearchEnabled: boolean;
	
	    static createFrom(source: any = {}) {
	        return new AIHealthSnapshot(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.settings = this.convertValues(source["settings"], domain.AISettings);
	        this.settingsPersisted = source["settingsPersisted"];
	        this.settingsUpdatedAt = this.convertValues(source["settingsUpdatedAt"], null);
	        this.semanticRuntime = this.convertValues(source["semanticRuntime"], ai.RuntimeStatus);
	        this.semanticIndex = this.convertValues(source["semanticIndex"], SemanticIndexStatus);
	        this.queue = this.convertValues(source["queue"], ai.JobQueueStatus);
	        this.shuttingDown = source["shuttingDown"];
	        this.semanticSearchEnabled = source["semanticSearchEnabled"];
	    }
	
		convertValues(a: any, classs: any, asMap: boolean = false): any {
		    if (!a) {
		        return a;
		    }
		    if (a.slice && a.map) {
		        return (a as any[]).map(elem => this.convertValues(elem, classs));
		    } else if ("object" === typeof a) {
		        if (asMap) {
		            for (const key of Object.keys(a)) {
		                a[key] = new classs(a[key]);
		            }
		            return a;
		        }
		        return new classs(a);
		    }
		    return a;
		}
	}
	export class AIStorageInfo {
	    mode: string;
	    rootPath: string;
	    dataPath: string;
	    databasePath: string;
	    logsPath: string;
	    modelsPath: string;
	    runtimesPath: string;
	    semanticIndexPath: string;
	    webviewDataPath: string;
	    preferredRootPath?: string;
	    legacyPath?: string;
	    legacyDetected: boolean;
	    usingLegacy: boolean;
	    migrationAvailable: boolean;
	    migrationPending: boolean;
	    migrationSourcePath?: string;
	    migrationTargetPath?: string;
	    migrationRequiresRestart: boolean;
	
	    static createFrom(source: any = {}) {
	        return new AIStorageInfo(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.mode = source["mode"];
	        this.rootPath = source["rootPath"];
	        this.dataPath = source["dataPath"];
	        this.databasePath = source["databasePath"];
	        this.logsPath = source["logsPath"];
	        this.modelsPath = source["modelsPath"];
	        this.runtimesPath = source["runtimesPath"];
	        this.semanticIndexPath = source["semanticIndexPath"];
	        this.webviewDataPath = source["webviewDataPath"];
	        this.preferredRootPath = source["preferredRootPath"];
	        this.legacyPath = source["legacyPath"];
	        this.legacyDetected = source["legacyDetected"];
	        this.usingLegacy = source["usingLegacy"];
	        this.migrationAvailable = source["migrationAvailable"];
	        this.migrationPending = source["migrationPending"];
	        this.migrationSourcePath = source["migrationSourcePath"];
	        this.migrationTargetPath = source["migrationTargetPath"];
	        this.migrationRequiresRestart = source["migrationRequiresRestart"];
	    }
	}
	export class AdvancedVisionCandidateInfo {
	    id: string;
	    version: string;
	    engine: string;
	    displayName: string;
	    license: string;
	    sizeBytes: number;
	    installed: boolean;
	
	    static createFrom(source: any = {}) {
	        return new AdvancedVisionCandidateInfo(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.id = source["id"];
	        this.version = source["version"];
	        this.engine = source["engine"];
	        this.displayName = source["displayName"];
	        this.license = source["license"];
	        this.sizeBytes = source["sizeBytes"];
	        this.installed = source["installed"];
	    }
	}
	export class AdvancedVisionResultDTO {
	    schemaVersion: number;
	    summary: string;
	    subjects: string[];
	    environment: string;
	    composition: string;
	    viewpoint: string;
	    actions: string[];
	    relationships: string[];
	    context: string;
	    differences: string[];
	    commonalities: string[];
	    reversePromptHints: string[];
	    visibleText: string[];
	    notes: string[];
	    completionTokens?: number;
	
	    static createFrom(source: any = {}) {
	        return new AdvancedVisionResultDTO(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.schemaVersion = source["schemaVersion"];
	        this.summary = source["summary"];
	        this.subjects = source["subjects"];
	        this.environment = source["environment"];
	        this.composition = source["composition"];
	        this.viewpoint = source["viewpoint"];
	        this.actions = source["actions"];
	        this.relationships = source["relationships"];
	        this.context = source["context"];
	        this.differences = source["differences"];
	        this.commonalities = source["commonalities"];
	        this.reversePromptHints = source["reversePromptHints"];
	        this.visibleText = source["visibleText"];
	        this.notes = source["notes"];
	        this.completionTokens = source["completionTokens"];
	    }
	}
	export class AdvancedVisionRunDTO {
	    id: number;
	    operation: string;
	    instruction: string;
	    state: string;
	    engine: string;
	    modelId: string;
	    modelVersion: string;
	    assetIds: number[];
	    result?: AdvancedVisionResultDTO;
	    errorMessage?: string;
	    createdAt: string;
	    completedAt?: string;
	
	    static createFrom(source: any = {}) {
	        return new AdvancedVisionRunDTO(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.id = source["id"];
	        this.operation = source["operation"];
	        this.instruction = source["instruction"];
	        this.state = source["state"];
	        this.engine = source["engine"];
	        this.modelId = source["modelId"];
	        this.modelVersion = source["modelVersion"];
	        this.assetIds = source["assetIds"];
	        this.result = this.convertValues(source["result"], AdvancedVisionResultDTO);
	        this.errorMessage = source["errorMessage"];
	        this.createdAt = source["createdAt"];
	        this.completedAt = source["completedAt"];
	    }
	
		convertValues(a: any, classs: any, asMap: boolean = false): any {
		    if (!a) {
		        return a;
		    }
		    if (a.slice && a.map) {
		        return (a as any[]).map(elem => this.convertValues(elem, classs));
		    } else if ("object" === typeof a) {
		        if (asMap) {
		            for (const key of Object.keys(a)) {
		                a[key] = new classs(a[key]);
		            }
		            return a;
		        }
		        return new classs(a);
		    }
		    return a;
		}
	}
	export class LightweightRuntimeInfo {
	    id: string;
	    version: string;
	    sizeBytes: number;
	    installed: boolean;
	    executablePath?: string;
	    platform: string;
	    architecture: string;
	
	    static createFrom(source: any = {}) {
	        return new LightweightRuntimeInfo(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.id = source["id"];
	        this.version = source["version"];
	        this.sizeBytes = source["sizeBytes"];
	        this.installed = source["installed"];
	        this.executablePath = source["executablePath"];
	        this.platform = source["platform"];
	        this.architecture = source["architecture"];
	    }
	}
	export class AdvancedVisionStatusInfo {
	    runtime: ai.RuntimeStatus;
	    llamaRuntime: LightweightRuntimeInfo;
	    models: AdvancedVisionCandidateInfo[];
	    activeModelId?: string;
	
	    static createFrom(source: any = {}) {
	        return new AdvancedVisionStatusInfo(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.runtime = this.convertValues(source["runtime"], ai.RuntimeStatus);
	        this.llamaRuntime = this.convertValues(source["llamaRuntime"], LightweightRuntimeInfo);
	        this.models = this.convertValues(source["models"], AdvancedVisionCandidateInfo);
	        this.activeModelId = source["activeModelId"];
	    }
	
		convertValues(a: any, classs: any, asMap: boolean = false): any {
		    if (!a) {
		        return a;
		    }
		    if (a.slice && a.map) {
		        return (a as any[]).map(elem => this.convertValues(elem, classs));
		    } else if ("object" === typeof a) {
		        if (asMap) {
		            for (const key of Object.keys(a)) {
		                a[key] = new classs(a[key]);
		            }
		            return a;
		        }
		        return new classs(a);
		    }
		    return a;
		}
	}
	export class AssetRelationDTO {
	    id: number;
	    parentAssetId: number;
	    parentFileName: string;
	    parentFilePath: string;
	    childAssetId: number;
	    childFileName: string;
	    childFilePath: string;
	    relationType: string;
	    note: string;
	    createdAt: string;
	
	    static createFrom(source: any = {}) {
	        return new AssetRelationDTO(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.id = source["id"];
	        this.parentAssetId = source["parentAssetId"];
	        this.parentFileName = source["parentFileName"];
	        this.parentFilePath = source["parentFilePath"];
	        this.childAssetId = source["childAssetId"];
	        this.childFileName = source["childFileName"];
	        this.childFilePath = source["childFilePath"];
	        this.relationType = source["relationType"];
	        this.note = source["note"];
	        this.createdAt = source["createdAt"];
	    }
	}
	export class GenerationGroupDTO {
	    id: number;
	    workId?: number;
	    name: string;
	    prompt: string;
	    negativePrompt: string;
	    modelName: string;
	    sampler: string;
	    scheduler: string;
	    steps: number;
	    cfgScale: number;
	    workflowJson: string;
	    notes: string;
	    assetIds: number[];
	    assets: CreativeAssetRefDTO[];
	    createdAt: string;
	    updatedAt: string;
	
	    static createFrom(source: any = {}) {
	        return new GenerationGroupDTO(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.id = source["id"];
	        this.workId = source["workId"];
	        this.name = source["name"];
	        this.prompt = source["prompt"];
	        this.negativePrompt = source["negativePrompt"];
	        this.modelName = source["modelName"];
	        this.sampler = source["sampler"];
	        this.scheduler = source["scheduler"];
	        this.steps = source["steps"];
	        this.cfgScale = source["cfgScale"];
	        this.workflowJson = source["workflowJson"];
	        this.notes = source["notes"];
	        this.assetIds = source["assetIds"];
	        this.assets = this.convertValues(source["assets"], CreativeAssetRefDTO);
	        this.createdAt = source["createdAt"];
	        this.updatedAt = source["updatedAt"];
	    }
	
		convertValues(a: any, classs: any, asMap: boolean = false): any {
		    if (!a) {
		        return a;
		    }
		    if (a.slice && a.map) {
		        return (a as any[]).map(elem => this.convertValues(elem, classs));
		    } else if ("object" === typeof a) {
		        if (asMap) {
		            for (const key of Object.keys(a)) {
		                a[key] = new classs(a[key]);
		            }
		            return a;
		        }
		        return new classs(a);
		    }
		    return a;
		}
	}
	export class CreativeAssetRefDTO {
	    id: number;
	    fileName: string;
	    filePath: string;
	
	    static createFrom(source: any = {}) {
	        return new CreativeAssetRefDTO(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.id = source["id"];
	        this.fileName = source["fileName"];
	        this.filePath = source["filePath"];
	    }
	}
	export class WorkDTO {
	    id: number;
	    title: string;
	    description: string;
	    coverAssetId?: number;
	    assetIds: number[];
	    assets: CreativeAssetRefDTO[];
	    createdAt: string;
	    updatedAt: string;
	
	    static createFrom(source: any = {}) {
	        return new WorkDTO(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.id = source["id"];
	        this.title = source["title"];
	        this.description = source["description"];
	        this.coverAssetId = source["coverAssetId"];
	        this.assetIds = source["assetIds"];
	        this.assets = this.convertValues(source["assets"], CreativeAssetRefDTO);
	        this.createdAt = source["createdAt"];
	        this.updatedAt = source["updatedAt"];
	    }
	
		convertValues(a: any, classs: any, asMap: boolean = false): any {
		    if (!a) {
		        return a;
		    }
		    if (a.slice && a.map) {
		        return (a as any[]).map(elem => this.convertValues(elem, classs));
		    } else if ("object" === typeof a) {
		        if (asMap) {
		            for (const key of Object.keys(a)) {
		                a[key] = new classs(a[key]);
		            }
		            return a;
		        }
		        return new classs(a);
		    }
		    return a;
		}
	}
	export class AssetCreativeContextDTO {
	    works: WorkDTO[];
	    groups: GenerationGroupDTO[];
	    relations: AssetRelationDTO[];
	
	    static createFrom(source: any = {}) {
	        return new AssetCreativeContextDTO(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.works = this.convertValues(source["works"], WorkDTO);
	        this.groups = this.convertValues(source["groups"], GenerationGroupDTO);
	        this.relations = this.convertValues(source["relations"], AssetRelationDTO);
	    }
	
		convertValues(a: any, classs: any, asMap: boolean = false): any {
		    if (!a) {
		        return a;
		    }
		    if (a.slice && a.map) {
		        return (a as any[]).map(elem => this.convertValues(elem, classs));
		    } else if ("object" === typeof a) {
		        if (asMap) {
		            for (const key of Object.keys(a)) {
		                a[key] = new classs(a[key]);
		            }
		            return a;
		        }
		        return new classs(a);
		    }
		    return a;
		}
	}
	export class TagDTO {
	    id: number;
	    name: string;
	    color: string;
	
	    static createFrom(source: any = {}) {
	        return new TagDTO(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.id = source["id"];
	        this.name = source["name"];
	        this.color = source["color"];
	    }
	}
	export class AssetDTO {
	    id: number;
	    libraryId: number;
	    folderPath: string;
	    fileName: string;
	    filePath: string;
	    extension: string;
	    fileSize: number;
	    createdAtFs?: string;
	    modifiedAtFs?: string;
	    width: number;
	    height: number;
	    mimeType?: string;
	    thumbStatus: string;
	    rating: number;
	    statusLabel: string;
	    isFavorite: boolean;
	    colorLabel?: string;
	    noteContent?: string;
	    tags?: TagDTO[];
	    cameraModel?: string;
	    lensModel?: string;
	    focalLength?: string;
	    aperture?: string;
	    shutterSpeed?: string;
	    iso: number;
	    exifDate?: string;
	    gpsLatitude?: string;
	    gpsLongitude?: string;
	    hashBlake3?: string;
	    semanticScore?: number;
	
	    static createFrom(source: any = {}) {
	        return new AssetDTO(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.id = source["id"];
	        this.libraryId = source["libraryId"];
	        this.folderPath = source["folderPath"];
	        this.fileName = source["fileName"];
	        this.filePath = source["filePath"];
	        this.extension = source["extension"];
	        this.fileSize = source["fileSize"];
	        this.createdAtFs = source["createdAtFs"];
	        this.modifiedAtFs = source["modifiedAtFs"];
	        this.width = source["width"];
	        this.height = source["height"];
	        this.mimeType = source["mimeType"];
	        this.thumbStatus = source["thumbStatus"];
	        this.rating = source["rating"];
	        this.statusLabel = source["statusLabel"];
	        this.isFavorite = source["isFavorite"];
	        this.colorLabel = source["colorLabel"];
	        this.noteContent = source["noteContent"];
	        this.tags = this.convertValues(source["tags"], TagDTO);
	        this.cameraModel = source["cameraModel"];
	        this.lensModel = source["lensModel"];
	        this.focalLength = source["focalLength"];
	        this.aperture = source["aperture"];
	        this.shutterSpeed = source["shutterSpeed"];
	        this.iso = source["iso"];
	        this.exifDate = source["exifDate"];
	        this.gpsLatitude = source["gpsLatitude"];
	        this.gpsLongitude = source["gpsLongitude"];
	        this.hashBlake3 = source["hashBlake3"];
	        this.semanticScore = source["semanticScore"];
	    }
	
		convertValues(a: any, classs: any, asMap: boolean = false): any {
		    if (!a) {
		        return a;
		    }
		    if (a.slice && a.map) {
		        return (a as any[]).map(elem => this.convertValues(elem, classs));
		    } else if ("object" === typeof a) {
		        if (asMap) {
		            for (const key of Object.keys(a)) {
		                a[key] = new classs(a[key]);
		            }
		            return a;
		        }
		        return new classs(a);
		    }
		    return a;
		}
	}
	export class GenerationLoRADTO {
	    name: string;
	    weight: number;
	    triggerWords: string[];
	
	    static createFrom(source: any = {}) {
	        return new GenerationLoRADTO(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.name = source["name"];
	        this.weight = source["weight"];
	        this.triggerWords = source["triggerWords"];
	    }
	}
	export class AssetGenerationMetadataDTO {
	    assetId: number;
	    present: boolean;
	    schemaVersion: number;
	    parserVersion: number;
	    sourceFormat: string;
	    positive: string;
	    negative: string;
	    checkpoint: string;
	    loras: GenerationLoRADTO[];
	    sampler: string;
	    scheduler: string;
	    cfg: number;
	    steps: number;
	    seed: number;
	    width: number;
	    height: number;
	    suggestedProfileId?: string;
	    rawPromptJson?: string;
	    rawWorkflowJson?: string;
	    parameters?: string;
	    rawJson: string;
	    parsedAt?: string;
	
	    static createFrom(source: any = {}) {
	        return new AssetGenerationMetadataDTO(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.assetId = source["assetId"];
	        this.present = source["present"];
	        this.schemaVersion = source["schemaVersion"];
	        this.parserVersion = source["parserVersion"];
	        this.sourceFormat = source["sourceFormat"];
	        this.positive = source["positive"];
	        this.negative = source["negative"];
	        this.checkpoint = source["checkpoint"];
	        this.loras = this.convertValues(source["loras"], GenerationLoRADTO);
	        this.sampler = source["sampler"];
	        this.scheduler = source["scheduler"];
	        this.cfg = source["cfg"];
	        this.steps = source["steps"];
	        this.seed = source["seed"];
	        this.width = source["width"];
	        this.height = source["height"];
	        this.suggestedProfileId = source["suggestedProfileId"];
	        this.rawPromptJson = source["rawPromptJson"];
	        this.rawWorkflowJson = source["rawWorkflowJson"];
	        this.parameters = source["parameters"];
	        this.rawJson = source["rawJson"];
	        this.parsedAt = source["parsedAt"];
	    }
	
		convertValues(a: any, classs: any, asMap: boolean = false): any {
		    if (!a) {
		        return a;
		    }
		    if (a.slice && a.map) {
		        return (a as any[]).map(elem => this.convertValues(elem, classs));
		    } else if ("object" === typeof a) {
		        if (asMap) {
		            for (const key of Object.keys(a)) {
		                a[key] = new classs(a[key]);
		            }
		            return a;
		        }
		        return new classs(a);
		    }
		    return a;
		}
	}
	export class AssetListRequest {
	    libraryId: number;
	    folderPath?: string;
	    recurse?: boolean;
	    search?: string;
	    rating?: number;
	    statusLabel?: string;
	    isFavorite?: boolean;
	    tagIds?: number[];
	    hasNote?: boolean;
	    extension?: string;
	    colorLabel?: string;
	    sortBy?: string;
	    sortDesc?: boolean;
	    offset: number;
	    limit: number;
	
	    static createFrom(source: any = {}) {
	        return new AssetListRequest(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.libraryId = source["libraryId"];
	        this.folderPath = source["folderPath"];
	        this.recurse = source["recurse"];
	        this.search = source["search"];
	        this.rating = source["rating"];
	        this.statusLabel = source["statusLabel"];
	        this.isFavorite = source["isFavorite"];
	        this.tagIds = source["tagIds"];
	        this.hasNote = source["hasNote"];
	        this.extension = source["extension"];
	        this.colorLabel = source["colorLabel"];
	        this.sortBy = source["sortBy"];
	        this.sortDesc = source["sortDesc"];
	        this.offset = source["offset"];
	        this.limit = source["limit"];
	    }
	}
	export class AssetListResponse {
	    assets: AssetDTO[];
	    totalCount: number;
	    semanticSearchSessionId?: string;
	
	    static createFrom(source: any = {}) {
	        return new AssetListResponse(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.assets = this.convertValues(source["assets"], AssetDTO);
	        this.totalCount = source["totalCount"];
	        this.semanticSearchSessionId = source["semanticSearchSessionId"];
	    }
	
		convertValues(a: any, classs: any, asMap: boolean = false): any {
		    if (!a) {
		        return a;
		    }
		    if (a.slice && a.map) {
		        return (a as any[]).map(elem => this.convertValues(elem, classs));
		    } else if ("object" === typeof a) {
		        if (asMap) {
		            for (const key of Object.keys(a)) {
		                a[key] = new classs(a[key]);
		            }
		            return a;
		        }
		        return new classs(a);
		    }
		    return a;
		}
	}
	
	export class CopyRequest {
	    assetIds: number[];
	    targetFolder: string;
	
	    static createFrom(source: any = {}) {
	        return new CopyRequest(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.assetIds = source["assetIds"];
	        this.targetFolder = source["targetFolder"];
	    }
	}
	export class CopyResult {
	    copiedCount: number;
	    failedIds: number[];
	    errors: string[];
	
	    static createFrom(source: any = {}) {
	        return new CopyResult(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.copiedCount = source["copiedCount"];
	        this.failedIds = source["failedIds"];
	        this.errors = source["errors"];
	    }
	}
	export class CreateGenerationGroupRequest {
	    assetIds: number[];
	    workId?: number;
	    name: string;
	    prompt: string;
	    negativePrompt: string;
	    modelName: string;
	    sampler: string;
	    scheduler: string;
	    steps: number;
	    cfgScale: number;
	    workflowJson: string;
	    notes: string;
	
	    static createFrom(source: any = {}) {
	        return new CreateGenerationGroupRequest(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.assetIds = source["assetIds"];
	        this.workId = source["workId"];
	        this.name = source["name"];
	        this.prompt = source["prompt"];
	        this.negativePrompt = source["negativePrompt"];
	        this.modelName = source["modelName"];
	        this.sampler = source["sampler"];
	        this.scheduler = source["scheduler"];
	        this.steps = source["steps"];
	        this.cfgScale = source["cfgScale"];
	        this.workflowJson = source["workflowJson"];
	        this.notes = source["notes"];
	    }
	}
	
	export class DeleteAssetFilesResult {
	    deletedCount: number;
	    failedCount: number;
	    deletedIds: number[];
	    failedIds: number[];
	    errors?: string[];
	
	    static createFrom(source: any = {}) {
	        return new DeleteAssetFilesResult(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.deletedCount = source["deletedCount"];
	        this.failedCount = source["failedCount"];
	        this.deletedIds = source["deletedIds"];
	        this.failedIds = source["failedIds"];
	        this.errors = source["errors"];
	    }
	}
	export class FolderDTO {
	    id: number;
	    libraryId: number;
	    path: string;
	    parentPath?: string;
	
	    static createFrom(source: any = {}) {
	        return new FolderDTO(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.id = source["id"];
	        this.libraryId = source["libraryId"];
	        this.path = source["path"];
	        this.parentPath = source["parentPath"];
	    }
	}
	
	
	export class ImagePromptSourceDTO {
	    kind: string;
	    label: string;
	    state: string;
	    engine?: string;
	    modelId?: string;
	    modelVersion?: string;
	    dataJson?: string;
	    note?: string;
	
	    static createFrom(source: any = {}) {
	        return new ImagePromptSourceDTO(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.kind = source["kind"];
	        this.label = source["label"];
	        this.state = source["state"];
	        this.engine = source["engine"];
	        this.modelId = source["modelId"];
	        this.modelVersion = source["modelVersion"];
	        this.dataJson = source["dataJson"];
	        this.note = source["note"];
	    }
	}
	export class ImagePromptResultDTO {
	    assetId: number;
	    targetProfileId: string;
	    positive: string;
	    negative: string;
	    characters: string[];
	    loras: string[];
	    composition: string;
	    notes: string[];
	    sources: ImagePromptSourceDTO[];
	    promptEngineUsed: boolean;
	    aiEngine?: string;
	    aiModelId?: string;
	    aiModelVersion?: string;
	
	    static createFrom(source: any = {}) {
	        return new ImagePromptResultDTO(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.assetId = source["assetId"];
	        this.targetProfileId = source["targetProfileId"];
	        this.positive = source["positive"];
	        this.negative = source["negative"];
	        this.characters = source["characters"];
	        this.loras = source["loras"];
	        this.composition = source["composition"];
	        this.notes = source["notes"];
	        this.sources = this.convertValues(source["sources"], ImagePromptSourceDTO);
	        this.promptEngineUsed = source["promptEngineUsed"];
	        this.aiEngine = source["aiEngine"];
	        this.aiModelId = source["aiModelId"];
	        this.aiModelVersion = source["aiModelVersion"];
	    }
	
		convertValues(a: any, classs: any, asMap: boolean = false): any {
		    if (!a) {
		        return a;
		    }
		    if (a.slice && a.map) {
		        return (a as any[]).map(elem => this.convertValues(elem, classs));
		    } else if ("object" === typeof a) {
		        if (asMap) {
		            for (const key of Object.keys(a)) {
		                a[key] = new classs(a[key]);
		            }
		            return a;
		        }
		        return new classs(a);
		    }
		    return a;
		}
	}
	export class PromptVersionDTO {
	    schemaVersion: number;
	    id: number;
	    variantId: number;
	    parentVersionId?: number;
	    positive: string;
	    negative: string;
	    source: string;
	    changeInstruction: string;
	    profileId: string;
	    profileSnapshotJson: string;
	    aiEngine: string;
	    aiModelId: string;
	    aiModelVersion: string;
	    metadataJson: string;
	    createdAt: string;
	
	    static createFrom(source: any = {}) {
	        return new PromptVersionDTO(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.schemaVersion = source["schemaVersion"];
	        this.id = source["id"];
	        this.variantId = source["variantId"];
	        this.parentVersionId = source["parentVersionId"];
	        this.positive = source["positive"];
	        this.negative = source["negative"];
	        this.source = source["source"];
	        this.changeInstruction = source["changeInstruction"];
	        this.profileId = source["profileId"];
	        this.profileSnapshotJson = source["profileSnapshotJson"];
	        this.aiEngine = source["aiEngine"];
	        this.aiModelId = source["aiModelId"];
	        this.aiModelVersion = source["aiModelVersion"];
	        this.metadataJson = source["metadataJson"];
	        this.createdAt = source["createdAt"];
	    }
	}
	export class PromptVariantDTO {
	    id: number;
	    projectId: number;
	    name: string;
	    versions: PromptVersionDTO[];
	    createdAt: string;
	
	    static createFrom(source: any = {}) {
	        return new PromptVariantDTO(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.id = source["id"];
	        this.projectId = source["projectId"];
	        this.name = source["name"];
	        this.versions = this.convertValues(source["versions"], PromptVersionDTO);
	        this.createdAt = source["createdAt"];
	    }
	
		convertValues(a: any, classs: any, asMap: boolean = false): any {
		    if (!a) {
		        return a;
		    }
		    if (a.slice && a.map) {
		        return (a as any[]).map(elem => this.convertValues(elem, classs));
		    } else if ("object" === typeof a) {
		        if (asMap) {
		            for (const key of Object.keys(a)) {
		                a[key] = new classs(a[key]);
		            }
		            return a;
		        }
		        return new classs(a);
		    }
		    return a;
		}
	}
	export class PromptLoRADTO {
	    name: string;
	    weight: number;
	    triggerWords: string[];
	
	    static createFrom(source: any = {}) {
	        return new PromptLoRADTO(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.name = source["name"];
	        this.weight = source["weight"];
	        this.triggerWords = source["triggerWords"];
	    }
	}
	export class PromptProjectDTO {
	    schemaVersion: number;
	    id: number;
	    title: string;
	    idea: string;
	    notes: string;
	    targetProfileId: string;
	    characters: string[];
	    loras: PromptLoRADTO[];
	    referenceAssetIds: number[];
	    relatedAssetIds: number[];
	    variants?: PromptVariantDTO[];
	    deleted: boolean;
	    createdAt: string;
	    updatedAt: string;
	
	    static createFrom(source: any = {}) {
	        return new PromptProjectDTO(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.schemaVersion = source["schemaVersion"];
	        this.id = source["id"];
	        this.title = source["title"];
	        this.idea = source["idea"];
	        this.notes = source["notes"];
	        this.targetProfileId = source["targetProfileId"];
	        this.characters = source["characters"];
	        this.loras = this.convertValues(source["loras"], PromptLoRADTO);
	        this.referenceAssetIds = source["referenceAssetIds"];
	        this.relatedAssetIds = source["relatedAssetIds"];
	        this.variants = this.convertValues(source["variants"], PromptVariantDTO);
	        this.deleted = source["deleted"];
	        this.createdAt = source["createdAt"];
	        this.updatedAt = source["updatedAt"];
	    }
	
		convertValues(a: any, classs: any, asMap: boolean = false): any {
		    if (!a) {
		        return a;
		    }
		    if (a.slice && a.map) {
		        return (a as any[]).map(elem => this.convertValues(elem, classs));
		    } else if ("object" === typeof a) {
		        if (asMap) {
		            for (const key of Object.keys(a)) {
		                a[key] = new classs(a[key]);
		            }
		            return a;
		        }
		        return new classs(a);
		    }
		    return a;
		}
	}
	export class ImagePromptProjectResultDTO {
	    project: PromptProjectDTO;
	    result: ImagePromptResultDTO;
	
	    static createFrom(source: any = {}) {
	        return new ImagePromptProjectResultDTO(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.project = this.convertValues(source["project"], PromptProjectDTO);
	        this.result = this.convertValues(source["result"], ImagePromptResultDTO);
	    }
	
		convertValues(a: any, classs: any, asMap: boolean = false): any {
		    if (!a) {
		        return a;
		    }
		    if (a.slice && a.map) {
		        return (a as any[]).map(elem => this.convertValues(elem, classs));
		    } else if ("object" === typeof a) {
		        if (asMap) {
		            for (const key of Object.keys(a)) {
		                a[key] = new classs(a[key]);
		            }
		            return a;
		        }
		        return new classs(a);
		    }
		    return a;
		}
	}
	export class ImagePromptRequestDTO {
	    assetId: number;
	    targetProfileId: string;
	    instruction?: string;
	    useAdvancedVision: boolean;
	
	    static createFrom(source: any = {}) {
	        return new ImagePromptRequestDTO(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.assetId = source["assetId"];
	        this.targetProfileId = source["targetProfileId"];
	        this.instruction = source["instruction"];
	        this.useAdvancedVision = source["useAdvancedVision"];
	    }
	}
	
	
	export class LibraryDTO {
	    id: number;
	    name: string;
	    rootPath: string;
	    isEnabled: boolean;
	    createdAt: string;
	    updatedAt: string;
	    lastScannedAt?: string;
	    assetCount: number;
	
	    static createFrom(source: any = {}) {
	        return new LibraryDTO(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.id = source["id"];
	        this.name = source["name"];
	        this.rootPath = source["rootPath"];
	        this.isEnabled = source["isEnabled"];
	        this.createdAt = source["createdAt"];
	        this.updatedAt = source["updatedAt"];
	        this.lastScannedAt = source["lastScannedAt"];
	        this.assetCount = source["assetCount"];
	    }
	}
	
	export class LightweightVisionResult {
	    schemaVersion: number;
	    shortCaption: string;
	    detailedCaption: string;
	    subject: string;
	    background: string;
	    composition: string;
	    viewpoint: string;
	    visibleText: string[];
	    notes: string[];
	    completionTokens?: number;
	
	    static createFrom(source: any = {}) {
	        return new LightweightVisionResult(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.schemaVersion = source["schemaVersion"];
	        this.shortCaption = source["shortCaption"];
	        this.detailedCaption = source["detailedCaption"];
	        this.subject = source["subject"];
	        this.background = source["background"];
	        this.composition = source["composition"];
	        this.viewpoint = source["viewpoint"];
	        this.visibleText = source["visibleText"];
	        this.notes = source["notes"];
	        this.completionTokens = source["completionTokens"];
	    }
	}
	export class LightweightVisionAnalysisDTO {
	    assetId: number;
	    state: string;
	    engine?: string;
	    modelId?: string;
	    modelVersion?: string;
	    result?: LightweightVisionResult;
	    errorMessage?: string;
	    analyzedAt?: string;
	    updatedAt: string;
	
	    static createFrom(source: any = {}) {
	        return new LightweightVisionAnalysisDTO(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.assetId = source["assetId"];
	        this.state = source["state"];
	        this.engine = source["engine"];
	        this.modelId = source["modelId"];
	        this.modelVersion = source["modelVersion"];
	        this.result = this.convertValues(source["result"], LightweightVisionResult);
	        this.errorMessage = source["errorMessage"];
	        this.analyzedAt = source["analyzedAt"];
	        this.updatedAt = source["updatedAt"];
	    }
	
		convertValues(a: any, classs: any, asMap: boolean = false): any {
		    if (!a) {
		        return a;
		    }
		    if (a.slice && a.map) {
		        return (a as any[]).map(elem => this.convertValues(elem, classs));
		    } else if ("object" === typeof a) {
		        if (asMap) {
		            for (const key of Object.keys(a)) {
		                a[key] = new classs(a[key]);
		            }
		            return a;
		        }
		        return new classs(a);
		    }
		    return a;
		}
	}
	export class LightweightVisionModelInfo {
	    id: string;
	    version: string;
	    engine: string;
	    displayName: string;
	    license: string;
	    sizeBytes: number;
	    installed: boolean;
	    runtime: ai.RuntimeStatus;
	    llamaRuntime: LightweightRuntimeInfo;
	
	    static createFrom(source: any = {}) {
	        return new LightweightVisionModelInfo(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.id = source["id"];
	        this.version = source["version"];
	        this.engine = source["engine"];
	        this.displayName = source["displayName"];
	        this.license = source["license"];
	        this.sizeBytes = source["sizeBytes"];
	        this.installed = source["installed"];
	        this.runtime = this.convertValues(source["runtime"], ai.RuntimeStatus);
	        this.llamaRuntime = this.convertValues(source["llamaRuntime"], LightweightRuntimeInfo);
	    }
	
		convertValues(a: any, classs: any, asMap: boolean = false): any {
		    if (!a) {
		        return a;
		    }
		    if (a.slice && a.map) {
		        return (a as any[]).map(elem => this.convertValues(elem, classs));
		    } else if ("object" === typeof a) {
		        if (asMap) {
		            for (const key of Object.keys(a)) {
		                a[key] = new classs(a[key]);
		            }
		            return a;
		        }
		        return new classs(a);
		    }
		    return a;
		}
	}
	
	export class ModelProfileDTO {
	    id: string;
	    name: string;
	    family: string;
	    checkpointName: string;
	    promptStyle: string;
	    qualityTags: string[];
	    negativePromptPolicy: string;
	    tagOrder: string[];
	    triggerWords: string[];
	    loraTriggerSyntax: string;
	    weightSyntax: string;
	    systemGuidance: string;
	    notes: string;
	    builtIn: boolean;
	    createdAt?: string;
	    updatedAt?: string;
	
	    static createFrom(source: any = {}) {
	        return new ModelProfileDTO(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.id = source["id"];
	        this.name = source["name"];
	        this.family = source["family"];
	        this.checkpointName = source["checkpointName"];
	        this.promptStyle = source["promptStyle"];
	        this.qualityTags = source["qualityTags"];
	        this.negativePromptPolicy = source["negativePromptPolicy"];
	        this.tagOrder = source["tagOrder"];
	        this.triggerWords = source["triggerWords"];
	        this.loraTriggerSyntax = source["loraTriggerSyntax"];
	        this.weightSyntax = source["weightSyntax"];
	        this.systemGuidance = source["systemGuidance"];
	        this.notes = source["notes"];
	        this.builtIn = source["builtIn"];
	        this.createdAt = source["createdAt"];
	        this.updatedAt = source["updatedAt"];
	    }
	}
	export class ModelProfileInput {
	    name: string;
	    family: string;
	    checkpointName: string;
	    promptStyle: string;
	    qualityTags: string[];
	    negativePromptPolicy: string;
	    tagOrder: string[];
	    triggerWords: string[];
	    loraTriggerSyntax: string;
	    weightSyntax: string;
	    systemGuidance: string;
	    notes: string;
	
	    static createFrom(source: any = {}) {
	        return new ModelProfileInput(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.name = source["name"];
	        this.family = source["family"];
	        this.checkpointName = source["checkpointName"];
	        this.promptStyle = source["promptStyle"];
	        this.qualityTags = source["qualityTags"];
	        this.negativePromptPolicy = source["negativePromptPolicy"];
	        this.tagOrder = source["tagOrder"];
	        this.triggerWords = source["triggerWords"];
	        this.loraTriggerSyntax = source["loraTriggerSyntax"];
	        this.weightSyntax = source["weightSyntax"];
	        this.systemGuidance = source["systemGuidance"];
	        this.notes = source["notes"];
	    }
	}
	export class MoveRequest {
	    assetIds: number[];
	    destinationFolder: string;
	    conflictPolicy: string;
	
	    static createFrom(source: any = {}) {
	        return new MoveRequest(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.assetIds = source["assetIds"];
	        this.destinationFolder = source["destinationFolder"];
	        this.conflictPolicy = source["conflictPolicy"];
	    }
	}
	export class MoveResult {
	    movedCount: number;
	    skippedCount: number;
	    failedCount: number;
	    errors?: string[];
	
	    static createFrom(source: any = {}) {
	        return new MoveResult(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.movedCount = source["movedCount"];
	        this.skippedCount = source["skippedCount"];
	        this.failedCount = source["failedCount"];
	        this.errors = source["errors"];
	    }
	}
	export class PostAccountDTO {
	    id: number;
	    postTargetId: number;
	    displayName: string;
	    accountIdentifier: string;
	    isActive: boolean;
	
	    static createFrom(source: any = {}) {
	        return new PostAccountDTO(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.id = source["id"];
	        this.postTargetId = source["postTargetId"];
	        this.displayName = source["displayName"];
	        this.accountIdentifier = source["accountIdentifier"];
	        this.isActive = source["isActive"];
	    }
	}
	export class PostDTO {
	    id: number;
	    title: string;
	    body: string;
	    hashtags: string;
	    status: string;
	    scheduledAt?: string;
	    publishedAt?: string;
	    assetIds?: number[];
	    createdAt: string;
	    updatedAt: string;
	
	    static createFrom(source: any = {}) {
	        return new PostDTO(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.id = source["id"];
	        this.title = source["title"];
	        this.body = source["body"];
	        this.hashtags = source["hashtags"];
	        this.status = source["status"];
	        this.scheduledAt = source["scheduledAt"];
	        this.publishedAt = source["publishedAt"];
	        this.assetIds = source["assetIds"];
	        this.createdAt = source["createdAt"];
	        this.updatedAt = source["updatedAt"];
	    }
	}
	export class PostRecordAssetDTO {
	    id: number;
	    fileName: string;
	    filePath: string;
	
	    static createFrom(source: any = {}) {
	        return new PostRecordAssetDTO(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.id = source["id"];
	        this.fileName = source["fileName"];
	        this.filePath = source["filePath"];
	    }
	}
	export class PostRecordDTO {
	    id: number;
	    title: string;
	    body: string;
	    hashtags: string;
	    platformMetadataJson: string;
	    status: string;
	    publishedAt?: string;
	    createdAt: string;
	    updatedAt: string;
	    assetIds: number[];
	    assets: PostRecordAssetDTO[];
	    targetId: number;
	    targetName: string;
	    targetKind: string;
	    accountId: number;
	    accountDisplay: string;
	    accountIdentifier: string;
	    externalPostId?: string;
	    externalUrl?: string;
	
	    static createFrom(source: any = {}) {
	        return new PostRecordDTO(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.id = source["id"];
	        this.title = source["title"];
	        this.body = source["body"];
	        this.hashtags = source["hashtags"];
	        this.platformMetadataJson = source["platformMetadataJson"];
	        this.status = source["status"];
	        this.publishedAt = source["publishedAt"];
	        this.createdAt = source["createdAt"];
	        this.updatedAt = source["updatedAt"];
	        this.assetIds = source["assetIds"];
	        this.assets = this.convertValues(source["assets"], PostRecordAssetDTO);
	        this.targetId = source["targetId"];
	        this.targetName = source["targetName"];
	        this.targetKind = source["targetKind"];
	        this.accountId = source["accountId"];
	        this.accountDisplay = source["accountDisplay"];
	        this.accountIdentifier = source["accountIdentifier"];
	        this.externalPostId = source["externalPostId"];
	        this.externalUrl = source["externalUrl"];
	    }
	
		convertValues(a: any, classs: any, asMap: boolean = false): any {
		    if (!a) {
		        return a;
		    }
		    if (a.slice && a.map) {
		        return (a as any[]).map(elem => this.convertValues(elem, classs));
		    } else if ("object" === typeof a) {
		        if (asMap) {
		            for (const key of Object.keys(a)) {
		                a[key] = new classs(a[key]);
		            }
		            return a;
		        }
		        return new classs(a);
		    }
		    return a;
		}
	}
	export class PostRecordRequest {
	    assetIds: number[];
	    targetId: number;
	    accountId: number;
	    title: string;
	    body: string;
	    hashtags: string;
	    platformMetadataJson: string;
	    externalPostId: string;
	    externalUrl: string;
	    publishedAt: string;
	
	    static createFrom(source: any = {}) {
	        return new PostRecordRequest(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.assetIds = source["assetIds"];
	        this.targetId = source["targetId"];
	        this.accountId = source["accountId"];
	        this.title = source["title"];
	        this.body = source["body"];
	        this.hashtags = source["hashtags"];
	        this.platformMetadataJson = source["platformMetadataJson"];
	        this.externalPostId = source["externalPostId"];
	        this.externalUrl = source["externalUrl"];
	        this.publishedAt = source["publishedAt"];
	    }
	}
	export class PostTargetDTO {
	    id: number;
	    name: string;
	    kind: string;
	
	    static createFrom(source: any = {}) {
	        return new PostTargetDTO(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.id = source["id"];
	        this.name = source["name"];
	        this.kind = source["kind"];
	    }
	}
	export class PromptEngineCandidateInfo {
	    id: string;
	    version: string;
	    engine: string;
	    displayName: string;
	    license: string;
	    sizeBytes: number;
	    installed: boolean;
	    reference: boolean;
	
	    static createFrom(source: any = {}) {
	        return new PromptEngineCandidateInfo(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.id = source["id"];
	        this.version = source["version"];
	        this.engine = source["engine"];
	        this.displayName = source["displayName"];
	        this.license = source["license"];
	        this.sizeBytes = source["sizeBytes"];
	        this.installed = source["installed"];
	        this.reference = source["reference"];
	    }
	}
	export class PromptEngineRequestDTO {
	    operation: string;
	    idea?: string;
	    positive?: string;
	    negative?: string;
	    sourceProfile?: string;
	    targetProfile?: string;
	    sourceProfileId?: string;
	    targetProfileId?: string;
	    instruction?: string;
	    contextJson?: string;
	    characters?: string[];
	    loras?: string[];
	
	    static createFrom(source: any = {}) {
	        return new PromptEngineRequestDTO(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.operation = source["operation"];
	        this.idea = source["idea"];
	        this.positive = source["positive"];
	        this.negative = source["negative"];
	        this.sourceProfile = source["sourceProfile"];
	        this.targetProfile = source["targetProfile"];
	        this.sourceProfileId = source["sourceProfileId"];
	        this.targetProfileId = source["targetProfileId"];
	        this.instruction = source["instruction"];
	        this.contextJson = source["contextJson"];
	        this.characters = source["characters"];
	        this.loras = source["loras"];
	    }
	}
	export class PromptEngineResultDTO {
	    positive: string;
	    negative: string;
	    characters: string[];
	    loras: string[];
	    composition: string;
	    notes: string[];
	    completionTokens?: number;
	    engine: string;
	    modelId: string;
	    modelVersion: string;
	
	    static createFrom(source: any = {}) {
	        return new PromptEngineResultDTO(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.positive = source["positive"];
	        this.negative = source["negative"];
	        this.characters = source["characters"];
	        this.loras = source["loras"];
	        this.composition = source["composition"];
	        this.notes = source["notes"];
	        this.completionTokens = source["completionTokens"];
	        this.engine = source["engine"];
	        this.modelId = source["modelId"];
	        this.modelVersion = source["modelVersion"];
	    }
	}
	export class PromptEngineStatusInfo {
	    runtime: ai.RuntimeStatus;
	    llamaRuntime: LightweightRuntimeInfo;
	    models: PromptEngineCandidateInfo[];
	    activeModelId?: string;
	    selectionNote: string;
	
	    static createFrom(source: any = {}) {
	        return new PromptEngineStatusInfo(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.runtime = this.convertValues(source["runtime"], ai.RuntimeStatus);
	        this.llamaRuntime = this.convertValues(source["llamaRuntime"], LightweightRuntimeInfo);
	        this.models = this.convertValues(source["models"], PromptEngineCandidateInfo);
	        this.activeModelId = source["activeModelId"];
	        this.selectionNote = source["selectionNote"];
	    }
	
		convertValues(a: any, classs: any, asMap: boolean = false): any {
		    if (!a) {
		        return a;
		    }
		    if (a.slice && a.map) {
		        return (a as any[]).map(elem => this.convertValues(elem, classs));
		    } else if ("object" === typeof a) {
		        if (asMap) {
		            for (const key of Object.keys(a)) {
		                a[key] = new classs(a[key]);
		            }
		            return a;
		        }
		        return new classs(a);
		    }
		    return a;
		}
	}
	
	
	export class PromptProjectInput {
	    title: string;
	    idea: string;
	    notes: string;
	    targetProfileId: string;
	    characters: string[];
	    loras: PromptLoRADTO[];
	    referenceAssetIds: number[];
	    relatedAssetIds: number[];
	
	    static createFrom(source: any = {}) {
	        return new PromptProjectInput(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.title = source["title"];
	        this.idea = source["idea"];
	        this.notes = source["notes"];
	        this.targetProfileId = source["targetProfileId"];
	        this.characters = source["characters"];
	        this.loras = this.convertValues(source["loras"], PromptLoRADTO);
	        this.referenceAssetIds = source["referenceAssetIds"];
	        this.relatedAssetIds = source["relatedAssetIds"];
	    }
	
		convertValues(a: any, classs: any, asMap: boolean = false): any {
		    if (!a) {
		        return a;
		    }
		    if (a.slice && a.map) {
		        return (a as any[]).map(elem => this.convertValues(elem, classs));
		    } else if ("object" === typeof a) {
		        if (asMap) {
		            for (const key of Object.keys(a)) {
		                a[key] = new classs(a[key]);
		            }
		            return a;
		        }
		        return new classs(a);
		    }
		    return a;
		}
	}
	
	
	export class PromptVersionInput {
	    variantId: number;
	    parentVersionId?: number;
	    positive: string;
	    negative: string;
	    source: string;
	    changeInstruction: string;
	    profileId: string;
	    aiEngine: string;
	    aiModelId: string;
	    aiModelVersion: string;
	    metadataJson: string;
	
	    static createFrom(source: any = {}) {
	        return new PromptVersionInput(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.variantId = source["variantId"];
	        this.parentVersionId = source["parentVersionId"];
	        this.positive = source["positive"];
	        this.negative = source["negative"];
	        this.source = source["source"];
	        this.changeInstruction = source["changeInstruction"];
	        this.profileId = source["profileId"];
	        this.aiEngine = source["aiEngine"];
	        this.aiModelId = source["aiModelId"];
	        this.aiModelVersion = source["aiModelVersion"];
	        this.metadataJson = source["metadataJson"];
	    }
	}
	
	export class SemanticModelInfo {
	    id: string;
	    version: string;
	    engine: string;
	    displayName: string;
	    license: string;
	    sizeBytes: number;
	    installed: boolean;
	    runtime: ai.RuntimeStatus;
	
	    static createFrom(source: any = {}) {
	        return new SemanticModelInfo(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.id = source["id"];
	        this.version = source["version"];
	        this.engine = source["engine"];
	        this.displayName = source["displayName"];
	        this.license = source["license"];
	        this.sizeBytes = source["sizeBytes"];
	        this.installed = source["installed"];
	        this.runtime = this.convertValues(source["runtime"], ai.RuntimeStatus);
	    }
	
		convertValues(a: any, classs: any, asMap: boolean = false): any {
		    if (!a) {
		        return a;
		    }
		    if (a.slice && a.map) {
		        return (a as any[]).map(elem => this.convertValues(elem, classs));
		    } else if ("object" === typeof a) {
		        if (asMap) {
		            for (const key of Object.keys(a)) {
		                a[key] = new classs(a[key]);
		            }
		            return a;
		        }
		        return new classs(a);
		    }
		    return a;
		}
	}
	

}

export namespace domain {
	
	export class AIAnalysis {
	    id: number;
	    assetId: number;
	    capability: string;
	    state: string;
	    engine: string;
	    modelId: string;
	    modelVersion: string;
	    resultJson: string;
	    errorMessage: string;
	    attemptCount: number;
	    // Go type: time
	    analyzedAt?: any;
	    // Go type: time
	    createdAt: any;
	    // Go type: time
	    updatedAt: any;
	
	    static createFrom(source: any = {}) {
	        return new AIAnalysis(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.id = source["id"];
	        this.assetId = source["assetId"];
	        this.capability = source["capability"];
	        this.state = source["state"];
	        this.engine = source["engine"];
	        this.modelId = source["modelId"];
	        this.modelVersion = source["modelVersion"];
	        this.resultJson = source["resultJson"];
	        this.errorMessage = source["errorMessage"];
	        this.attemptCount = source["attemptCount"];
	        this.analyzedAt = this.convertValues(source["analyzedAt"], null);
	        this.createdAt = this.convertValues(source["createdAt"], null);
	        this.updatedAt = this.convertValues(source["updatedAt"], null);
	    }
	
		convertValues(a: any, classs: any, asMap: boolean = false): any {
		    if (!a) {
		        return a;
		    }
		    if (a.slice && a.map) {
		        return (a as any[]).map(elem => this.convertValues(elem, classs));
		    } else if ("object" === typeof a) {
		        if (asMap) {
		            for (const key of Object.keys(a)) {
		                a[key] = new classs(a[key]);
		            }
		            return a;
		        }
		        return new classs(a);
		    }
		    return a;
		}
	}
	export class AIJob {
	    id: number;
	    assetId: number;
	    capability: string;
	    source: string;
	    priority: number;
	    status: string;
	    attemptCount: number;
	    maxAttempts: number;
	    lastError: string;
	    cancelRequested: boolean;
	    // Go type: time
	    createdAt: any;
	    // Go type: time
	    startedAt?: any;
	    // Go type: time
	    finishedAt?: any;
	
	    static createFrom(source: any = {}) {
	        return new AIJob(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.id = source["id"];
	        this.assetId = source["assetId"];
	        this.capability = source["capability"];
	        this.source = source["source"];
	        this.priority = source["priority"];
	        this.status = source["status"];
	        this.attemptCount = source["attemptCount"];
	        this.maxAttempts = source["maxAttempts"];
	        this.lastError = source["lastError"];
	        this.cancelRequested = source["cancelRequested"];
	        this.createdAt = this.convertValues(source["createdAt"], null);
	        this.startedAt = this.convertValues(source["startedAt"], null);
	        this.finishedAt = this.convertValues(source["finishedAt"], null);
	    }
	
		convertValues(a: any, classs: any, asMap: boolean = false): any {
		    if (!a) {
		        return a;
		    }
		    if (a.slice && a.map) {
		        return (a as any[]).map(elem => this.convertValues(elem, classs));
		    } else if ("object" === typeof a) {
		        if (asMap) {
		            for (const key of Object.keys(a)) {
		                a[key] = new classs(a[key]);
		            }
		            return a;
		        }
		        return new classs(a);
		    }
		    return a;
		}
	}
	export class AISettings {
	    enabled: boolean;
	    semanticSearch: boolean;
	    tagger: boolean;
	    lightweightVision: boolean;
	    advancedVision: boolean;
	    promptEngine: boolean;
	    autoAnalyze: boolean;
	    gpuAcceleration: boolean;
	
	    static createFrom(source: any = {}) {
	        return new AISettings(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.enabled = source["enabled"];
	        this.semanticSearch = source["semanticSearch"];
	        this.tagger = source["tagger"];
	        this.lightweightVision = source["lightweightVision"];
	        this.advancedVision = source["advancedVision"];
	        this.promptEngine = source["promptEngine"];
	        this.autoAnalyze = source["autoAnalyze"];
	        this.gpuAcceleration = source["gpuAcceleration"];
	    }
	}

}

export namespace llamacpp {
	
	export class RuntimeStore {
	
	
	    static createFrom(source: any = {}) {
	        return new RuntimeStore(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	
	    }
	}

}

export namespace scanner {
	
	export class ImageEntry {
	    filePath: string;
	    fileName: string;
	    folderPath: string;
	    extension: string;
	    fileSize: number;
	
	    static createFrom(source: any = {}) {
	        return new ImageEntry(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.filePath = source["filePath"];
	        this.fileName = source["fileName"];
	        this.folderPath = source["folderPath"];
	        this.extension = source["extension"];
	        this.fileSize = source["fileSize"];
	    }
	}
	export class FolderScanResult {
	    images: ImageEntry[];
	    totalCount: number;
	    hasMore: boolean;
	
	    static createFrom(source: any = {}) {
	        return new FolderScanResult(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.images = this.convertValues(source["images"], ImageEntry);
	        this.totalCount = source["totalCount"];
	        this.hasMore = source["hasMore"];
	    }
	
		convertValues(a: any, classs: any, asMap: boolean = false): any {
		    if (!a) {
		        return a;
		    }
		    if (a.slice && a.map) {
		        return (a as any[]).map(elem => this.convertValues(elem, classs));
		    } else if ("object" === typeof a) {
		        if (asMap) {
		            for (const key of Object.keys(a)) {
		                a[key] = new classs(a[key]);
		            }
		            return a;
		        }
		        return new classs(a);
		    }
		    return a;
		}
	}
	
	export class SyncResult {
	    libraryId: number;
	    scannedCount: number;
	    addedCount: number;
	    updatedCount: number;
	    removedCount: number;
	    skippedCount: number;
	    failedCount: number;
	    changed: boolean;
	
	    static createFrom(source: any = {}) {
	        return new SyncResult(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.libraryId = source["libraryId"];
	        this.scannedCount = source["scannedCount"];
	        this.addedCount = source["addedCount"];
	        this.updatedCount = source["updatedCount"];
	        this.removedCount = source["removedCount"];
	        this.skippedCount = source["skippedCount"];
	        this.failedCount = source["failedCount"];
	        this.changed = source["changed"];
	    }
	}

}

