package commands

type AIStorageInfo struct {
	ModelsPath   string `json:"modelsPath"`
	RuntimesPath string `json:"runtimesPath"`
}

func (c *AppCommands) GetAIStorageInfo() AIStorageInfo {
	info := AIStorageInfo{}
	if c.aiManager != nil && c.aiManager.Store() != nil {
		info.ModelsPath = c.aiManager.Store().Root()
	}
	if c.llamaRuntimeStore != nil {
		info.RuntimesPath = c.llamaRuntimeStore.Root()
	}
	return info
}
