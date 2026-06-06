export namespace commands {
	
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
	
	    static createFrom(source: any = {}) {
	        return new AssetListResponse(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.assets = this.convertValues(source["assets"], AssetDTO);
	        this.totalCount = source["totalCount"];
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

}

