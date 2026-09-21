package siglip2

type directMLAdapterCandidate struct {
	ID                   int
	Name                 string
	DedicatedVideoMemory uint64
	Software             bool
}

func chooseDirectMLAdapter(candidates []directMLAdapterCandidate) (directMLAdapterCandidate, bool) {
	var selected directMLAdapterCandidate
	found := false
	for _, candidate := range candidates {
		if candidate.Software {
			continue
		}
		if !found ||
			candidate.DedicatedVideoMemory > selected.DedicatedVideoMemory ||
			(candidate.DedicatedVideoMemory == selected.DedicatedVideoMemory && candidate.ID < selected.ID) {
			selected = candidate
			found = true
		}
	}
	return selected, found
}
