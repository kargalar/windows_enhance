// WinUI controls use different interfaces from Windows.UI.Xaml. Use the XAML
// diagnostics property API for the small Explorer command-bar surface instead.
struct ExplorerPropertyChain {
    unsigned int sourceCount{}, count{};
    PropertyChainSource* sources{};
    PropertyChainValue* values{};
    ExplorerPropertyChain(IVisualTreeService* service, InstanceHandle handle) {
        winrt::check_hresult(service->GetPropertyValuesChain(handle, &sourceCount, &sources, &count, &values));
    }
    ~ExplorerPropertyChain() {
        for (unsigned int i = 0; i < sourceCount; ++i) {
            SysFreeString(sources[i].TargetType); SysFreeString(sources[i].Name);
            SysFreeString(sources[i].SrcInfo.FileName); SysFreeString(sources[i].SrcInfo.Hash);
        }
        for (unsigned int i = 0; i < count; ++i) {
            auto& p = values[i];
            SysFreeString(p.Type); SysFreeString(p.DeclaringType); SysFreeString(p.ValueType);
            SysFreeString(p.ItemType); SysFreeString(p.Value); SysFreeString(p.PropertyName);
        }
        CoTaskMemFree(sources); CoTaskMemFree(values);
    }
    ExplorerPropertyChain(ExplorerPropertyChain const&) = delete;
};
struct ExplorerSavedProperty {
    unsigned int index{};
    InstanceHandle original{};
    bool local{};
    std::wstring name;
};
struct ExplorerCommandBarState {
    std::vector<ExplorerSavedProperty> saved;
    static bool Supports(std::wstring const& type) {
        return type == L"FileExplorerExtensions.CommandBarControl" ||
            type == L"FileExplorerExtensions.CommandBarControl_Wave1" ||
            type == L"FileExplorerExtensions.NavigationBarControl";
    }
    static InstanceHandle Value(IVisualTreeService* service, wchar_t const* type, wchar_t const* text) {
        InstanceHandle handle{};
        auto typeString = SysAllocString(type), valueString = SysAllocString(text);
        auto result = service->CreateInstance(typeString, valueString, &handle);
        SysFreeString(typeString); SysFreeString(valueString);
        winrt::check_hresult(result);
        return handle;
    }
    void Restore(IVisualTreeService* service, InstanceHandle handle) {
        for (auto it = saved.rbegin(); it != saved.rend(); ++it) {
            winrt::check_hresult(it->local ? service->SetProperty(handle, it->original, it->index) : service->ClearProperty(handle, it->index));
        }
        saved.clear();
    }
    void Apply(IVisualTreeService* service, InstanceHandle handle, std::map<std::wstring, std::wstring> const& desired) {
        if (desired.empty()) { Restore(service, handle); return; }
        ExplorerPropertyChain chain(service, handle);
        for (auto const& [name, text] : desired) {
            if (name != L"Visibility" && name != L"Grid.RowSpan") continue;
            auto propertyName = name == L"Grid.RowSpan" ? L"RowSpan" : L"Visibility";
            PropertyChainValue* property{};
            for (unsigned int i = 0; i < chain.count; ++i) {
                auto& p = chain.values[i];
                if (p.PropertyName && (std::wstring(p.PropertyName) == propertyName || std::wstring(p.PropertyName) == name) && !p.Overridden) { property = &p; break; }
            }
            if (!property) {
                throw winrt::hresult_invalid_argument(L"Explorer property is unavailable: " + name);
            }
            auto previous = std::find_if(saved.begin(), saved.end(), [&](auto const& p) { return p.name == name; });
            if (previous == saved.end()) {
                ExplorerSavedProperty original;
                original.index = property->Index;
                original.name = name;
                original.local = property->PropertyChainIndex < chain.sourceCount && chain.sources[property->PropertyChainIndex].Source == BaseValueSourceLocal;
                // Preserve object and binding handles rather than converting their evaluated text.
                if (original.local) {
                    if (property->MetadataBits & IsValueHandle) original.original = wcstoull(property->Value, nullptr, 10);
                    else original.original = Value(service, property->Type, property->Value);
                }
                saved.push_back(original);
            }
            auto value = Value(service, name == L"Visibility" ? L"Microsoft.UI.Xaml.Visibility" : L"Int32", text.c_str());
            winrt::check_hresult(service->SetProperty(handle, value, property->Index));
        }
    }
};
