#include <windows.h>
#include <ocidl.h>
#include <xamlom.h>
#include <windows.ui.xaml.hosting.desktopwindowxamlsource.h>
#undef GetCurrentTime
#include <winrt/base.h>
#include <winrt/Windows.Data.Json.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.UI.h>
#include <winrt/Windows.UI.Core.h>
#include <winrt/Windows.UI.Xaml.h>
#include <winrt/Windows.UI.Xaml.Controls.h>
#include <winrt/Windows.UI.Xaml.Media.h>
#include <winrt/Windows.UI.Xaml.Markup.h>
#include <winrt/Windows.UI.Xaml.Automation.h>
#include <winrt/Windows.UI.Xaml.Automation.Peers.h>
#include <winrt/Windows.UI.Xaml.Shapes.h>
#include <winrt/Windows.UI.Xaml.Data.h>
#include <winrt/Windows.UI.Xaml.Hosting.h>
#include <algorithm>
#include <atomic>
#include <cmath>
#include <map>
#include <mutex>
#include <thread>
#include <vector>
#include "BridgeProtocol.h"
#include "TaskbarGeometry.h"

using namespace winrt;
using namespace Windows::Data::Json;
using namespace Windows::Foundation;
using namespace Windows::UI;
using namespace Windows::UI::Xaml;
using namespace Windows::UI::Xaml::Controls;
using namespace Windows::UI::Xaml::Media;

namespace winforge::shell {
using XamlValue = winrt::Windows::Foundation::IInspectable;
}
#include "TaskbarDockAnimation.h"
namespace winforge::shell {
struct SavedProperty {
    DependencyProperty property{nullptr};
    XamlValue value{nullptr};
    Data::BindingBase binding{nullptr};
    std::wstring name;
    std::wstring desired;
};
struct Node {
    InstanceHandle handle{};
    InstanceHandle parent{};
    std::wstring type;
    std::wstring name;
    weak_ref<FrameworkElement> element;
    std::vector<SavedProperty> saved;
    std::unique_ptr<TaskbarGeometry> geometry;
    std::unique_ptr<TaskbarDockAnimation> dock;
    ColumnDefinition iconColumn{nullptr};
    XamlValue originalIconColumnWidth{nullptr};
    double iconColumnWidth{};
};
struct SnapshotNode {
    InstanceHandle parent{};
    std::wstring type;
    std::wstring name;
    std::wstring automationId;
    std::wstring accessibleName;
    double width{};
    double height{};
    double opacity{};
    std::wstring visibility;
    std::wstring text;
    std::wstring fill;
    Rect screenBounds{};
    double hostHeight{};
    bool dockAnimation{};
    double dockScale{1.0};
};

class Tap;
struct UiContext {
    Tap* owner{};
    HWND window{};
    LONG revision{-1};
    std::map<InstanceHandle, Node> nodes;
    JsonObject configuration{nullptr};
};

class Tap : public implements<Tap, IObjectWithSite, IVisualTreeServiceCallback2> {
    com_ptr<IUnknown> site_;
    com_ptr<IXamlDiagnostics> diagnostics_;
    com_ptr<IVisualTreeService> service_;
    SharedSession session_;
    std::recursive_mutex lifetime_;
    std::atomic_bool stopped_{};
    std::mutex mutex_;
    std::mutex snapshotMutex_;
    std::map<DWORD, std::unique_ptr<UiContext>> contexts_;
    std::map<InstanceHandle, SnapshotNode> snapshot_;
    std::map<InstanceHandle, HWND> rootWindows_;
    static constexpr UINT_PTR TimerId = 1;
    static constexpr UINT PollMilliseconds = 200;

    void RecordError(HRESULT result = E_FAIL, std::wstring const& message = {}) noexcept {
        std::lock_guard lock(lifetime_);
        if (auto header = session_.Get()) {
            wcsncpy_s(header->lastErrorMessage, message.c_str(), _TRUNCATE);
            InterlockedExchange(&header->lastError, result);
            InterlockedIncrement(&header->errors);
        }
    }

    static LRESULT CALLBACK WindowProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam) noexcept {
        auto context = reinterpret_cast<UiContext*>(GetWindowLongPtrW(window, GWLP_USERDATA));
        if (message == WM_NCCREATE) {
            context = static_cast<UiContext*>(reinterpret_cast<CREATESTRUCTW*>(lParam)->lpCreateParams);
            SetWindowLongPtrW(window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(context));
        }
        if (message == WM_TIMER && context) { context->owner->Poll(*context); return 0; }
        if (message == WM_APP + 2 && context) {
            KillTimer(window, TimerId);
            for (auto& [handle, node] : context->nodes) { (void)handle; context->owner->Restore(node); }
            context->nodes.clear();
            context->configuration = nullptr;
            DestroyWindow(window);
            context->window = nullptr;
            return 0;
        }
        if (message == WM_APP + 1 && context) {
            auto handle = static_cast<InstanceHandle>(wParam);
            auto found = context->nodes.find(handle);
            if (found != context->nodes.end()) { context->owner->Restore(found->second); context->nodes.erase(found); }
            return 0;
        }
        return DefWindowProcW(window, message, wParam, lParam);
    }

    UiContext& Context() {
        std::lock_guard lock(mutex_);
        auto thread = GetCurrentThreadId();
        auto& context = contexts_[thread];
        if (!context) {
            context = std::make_unique<UiContext>();
            context->owner = this;
            WNDCLASSW definition{};
            definition.lpfnWndProc = WindowProc;
            definition.hInstance = Module();
            definition.lpszClassName = L"WinForge.Shell.Diagnostics";
            RegisterClassW(&definition);
            context->window = CreateWindowExW(0, definition.lpszClassName, L"", 0, 0, 0, 0, 0,
                HWND_MESSAGE, nullptr, Module(), context.get());
            if (!context->window) throw_last_error();
            if (!SetTimer(context->window, TimerId, PollMilliseconds, nullptr)) throw_last_error();
        }
        return *context;
    }

    static HMODULE Module() {
        HMODULE module{};
        GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
            reinterpret_cast<LPCWSTR>(&WindowProc), &module);
        return module;
    }

    void RestoreProperty(FrameworkElement const& element, SavedProperty const& saved) {
        if (saved.binding) element.SetBinding(saved.property, saved.binding);
        else if (saved.value == DependencyProperty::UnsetValue()) element.ClearValue(saved.property);
        else element.SetValue(saved.property, saved.value);
        InterlockedIncrement(&session_.Get()->restored);
    }

    static void RestoreIconColumn(Node& node) {
        if (!node.iconColumn) return;
        if (node.originalIconColumnWidth == DependencyProperty::UnsetValue()) node.iconColumn.ClearValue(ColumnDefinition::WidthProperty());
        else node.iconColumn.SetValue(ColumnDefinition::WidthProperty(), node.originalIconColumnWidth);
        node.iconColumn = nullptr;
        node.originalIconColumnWidth = nullptr;
        node.iconColumnWidth = 0;
    }

    void Restore(Node& node) noexcept {
        try {
            node.dock.reset();
            RestoreIconColumn(node);
            auto element = node.element.get();
            if (!element) { node.saved.clear(); return; }
            for (auto iterator = node.saved.rbegin(); iterator != node.saved.rend(); ++iterator) {
                RestoreProperty(element, *iterator);
            }
            node.saved.clear();
            if (node.geometry) {
                if (!node.geometry->Restore()) RecordError(HRESULT_FROM_WIN32(GetLastError()));
                node.geometry.reset();
            }
        } catch (...) { RecordError(to_hresult()); }
    }

    static bool MatchIdentity(std::wstring const& selector, std::wstring const& actualType,
        std::wstring const& actualName, std::wstring const& automationId) {
        auto filter = selector.find(L"[AutomationId=");
        auto target = selector.substr(0, filter);
        if (filter != std::wstring::npos) {
            if (!selector.ends_with(L"]")) return false;
            auto expected = selector.substr(filter + 14, selector.size() - filter - 15);
            if (automationId != expected) return false;
        }
        auto hash = target.find(L'#');
        auto type = target.substr(0, hash);
        auto name = hash == std::wstring::npos ? L"" : target.substr(hash + 1);
        return (type.empty() || type == L"*" || type == actualType ||
            (actualType.size() > type.size() && actualType.ends_with(L"." + type))) &&
            (name.empty() || name == actualName);
    }

    bool Match(std::wstring const& selector, Node const& node) {
        if (selector.starts_with(L"@")) {
            wchar_t* end{};
            auto handle = wcstoull(selector.c_str() + 1, &end, 10);
            return end != selector.c_str() + 1 && !*end && handle == node.handle;
        }
        auto element = node.element.get();
        if (!element) return false;
        auto separator = selector.find(L' ');
        auto target = separator == std::wstring::npos ? selector : selector.substr(separator + 1);
        // Restrict an items panel to its nearest owning ItemsControl, excluding nested pinned grids.
        auto ownerFilter = target.find(L"[ItemsControl=");
        if (ownerFilter != std::wstring::npos) {
            if (!target.ends_with(L"]")) return false;
            auto ownerName = target.substr(ownerFilter + 14, target.size() - ownerFilter - 15);
            target = target.substr(0, ownerFilter);
            auto parent = Media::VisualTreeHelper::GetParent(element);
            ItemsControl owner{nullptr};
            while (parent && !owner) {
                owner = parent.try_as<ItemsControl>();
                if (!owner) parent = Media::VisualTreeHelper::GetParent(parent);
            }
            if (!owner || std::wstring(owner.Name()) != ownerName) return false;
        }
        if (!MatchIdentity(target, node.type, node.name, std::wstring(Automation::AutomationProperties::GetAutomationId(element)))) return false;
        if (separator == std::wstring::npos) return true;
        auto ancestorSelector = selector.substr(0, separator);
        std::lock_guard lock(mutex_);
        auto ancestor = node.parent;
        for (unsigned int depth = 0; ancestor && depth < 64; ++depth) {
            auto found = snapshot_.find(ancestor);
            if (found == snapshot_.end()) return false;
            auto const& parent = found->second;
            if (MatchIdentity(ancestorSelector, parent.type, parent.name, parent.automationId)) return true;
            ancestor = parent.parent;
        }
        return false;
    }

    static XamlValue Brush(std::wstring const& text) {
        if (text.size() != 7 && text.size() != 9) throw hresult_invalid_argument();
        if (text[0] != L'#') throw hresult_invalid_argument();
        wchar_t* end{};
        auto color = wcstoul(text.c_str() + 1, &end, 16);
        if (*end) throw hresult_invalid_argument();
        return SolidColorBrush(Color{static_cast<BYTE>(text.size() == 9 ? color >> 24 : 255),
            static_cast<BYTE>(color >> 16), static_cast<BYTE>(color >> 8), static_cast<BYTE>(color)});
    }

    static Thickness ThicknessValue(std::wstring const& text) {
        std::vector<double> values;
        std::size_t start = 0;
        while (start <= text.size()) {
            auto end = text.find(L',', start);
            auto item = text.substr(start, end == std::wstring::npos ? end : end - start);
            wchar_t* tail{};
            auto number = wcstod(item.c_str(), &tail);
            if (tail == item.c_str() || *tail || !std::isfinite(number)) throw hresult_invalid_argument();
            values.push_back(number);
            if (end == std::wstring::npos) break;
            start = end + 1;
        }
        if (values.size() == 1) return Thickness{values[0], values[0], values[0], values[0]};
        if (values.size() == 2) return Thickness{values[0], values[1], values[0], values[1]};
        if (values.size() == 4) return Thickness{values[0], values[1], values[2], values[3]};
        throw hresult_invalid_argument();
    }

    static std::pair<DependencyProperty, XamlValue> Property(FrameworkElement const& element,
        std::wstring const& name, std::wstring const& text) {
        DependencyProperty property{nullptr};
        XamlValue value{nullptr};
        if (name == L"Width") property = FrameworkElement::WidthProperty();
        else if (name == L"Height") property = FrameworkElement::HeightProperty();
        else if (name == L"MinWidth") property = FrameworkElement::MinWidthProperty();
        else if (name == L"MinHeight") property = FrameworkElement::MinHeightProperty();
        else if (name == L"MaxWidth") property = FrameworkElement::MaxWidthProperty();
        else if (name == L"MaxHeight") property = FrameworkElement::MaxHeightProperty();
        else if (name == L"Opacity") property = UIElement::OpacityProperty();
        if (property) {
            wchar_t* end{};
            auto number = wcstod(text.c_str(), &end);
            if (end == text.c_str() || *end || !std::isfinite(number)) throw hresult_invalid_argument();
            value = box_value(number);
        } else if (name == L"Visibility") {
            property = UIElement::VisibilityProperty();
            if (text != L"Visible" && text != L"Collapsed") throw hresult_invalid_argument();
            value = box_value(text == L"Collapsed" ? Visibility::Collapsed : Visibility::Visible);
        } else if (name == L"Margin") {
            property = FrameworkElement::MarginProperty(); value = box_value(ThicknessValue(text));
        } else if (name == L"HorizontalAlignment" || name == L"HorizontalContentAlignment") {
            HorizontalAlignment alignment;
            if (text == L"Left") alignment = HorizontalAlignment::Left;
            else if (text == L"Center") alignment = HorizontalAlignment::Center;
            else if (text == L"Right") alignment = HorizontalAlignment::Right;
            else if (text == L"Stretch") alignment = HorizontalAlignment::Stretch;
            else throw hresult_invalid_argument();
            if (name == L"HorizontalAlignment") property = FrameworkElement::HorizontalAlignmentProperty();
            else if (element.try_as<Control>()) property = Control::HorizontalContentAlignmentProperty();
            value = box_value(alignment);
        } else if (name == L"VerticalAlignment" || name == L"VerticalContentAlignment") {
            VerticalAlignment alignment;
            if (text == L"Top") alignment = VerticalAlignment::Top;
            else if (text == L"Center") alignment = VerticalAlignment::Center;
            else if (text == L"Bottom") alignment = VerticalAlignment::Bottom;
            else if (text == L"Stretch") alignment = VerticalAlignment::Stretch;
            else throw hresult_invalid_argument();
            if (name == L"VerticalAlignment") property = FrameworkElement::VerticalAlignmentProperty();
            else if (element.try_as<Control>()) property = Control::VerticalContentAlignmentProperty();
            value = box_value(alignment);
        } else if (name == L"Background") {
            if (element.try_as<Border>()) property = Border::BackgroundProperty();
            else if (element.try_as<Panel>()) property = Panel::BackgroundProperty();
            else if (element.try_as<Control>()) property = Control::BackgroundProperty();
            value = Brush(text);
        } else if (name == L"Fill" || name == L"Stroke") {
            if (element.try_as<Shapes::Shape>()) property = name == L"Fill" ? Shapes::Shape::FillProperty() : Shapes::Shape::StrokeProperty();
            value = Brush(text);
        } else if (name == L"Foreground") {
            if (element.try_as<TextBlock>()) property = TextBlock::ForegroundProperty();
            else if (element.try_as<Control>()) property = Control::ForegroundProperty();
            value = Brush(text);
        } else if (name == L"Text" && element.try_as<TextBlock>()) {
            property = TextBlock::TextProperty(); value = box_value(hstring(text));
        } else if (name == L"ItemsPanel" && element.try_as<ItemsControl>()) {
            property = ItemsControl::ItemsPanelProperty(); value = Markup::XamlReader::Load(text);
        } else if (name == L"CornerRadius") {
            if (element.try_as<Border>()) property = Border::CornerRadiusProperty();
            else if (element.try_as<Control>()) property = Control::CornerRadiusProperty();
            auto values = ThicknessValue(text);
            value = box_value(CornerRadius{values.Left, values.Top, values.Right, values.Bottom});
        } else if (name == L"Padding") {
            if (element.try_as<Border>()) property = Border::PaddingProperty();
            else if (element.try_as<Control>()) property = Control::PaddingProperty();
            value = box_value(ThicknessValue(text));
        }
        return {property, value};
    }

    void Apply(UiContext& context, Node& node) noexcept {
        try {
            auto element = node.element.get();
            if (!element) return;
            if (!context.configuration || !context.configuration.GetNamedBoolean(L"active", false)) { Restore(node); return; }
            auto rules = context.configuration.GetNamedArray(L"rules", JsonArray{});
            std::map<std::wstring, std::wstring> desired;
            for (auto const& item : rules) {
                auto rule = item.GetObject();
                if (!Match(std::wstring(rule.GetNamedString(L"selector")), node)) continue;
                auto properties = rule.GetNamedObject(L"properties");
                for (auto const& pair : properties) {
                    desired[std::wstring(pair.Key())] = std::wstring(pair.Value().GetString());
                }
            }
            for (auto index = node.saved.size(); index > 0; --index) {
                auto iterator = node.saved.begin() + static_cast<std::ptrdiff_t>(index - 1);
                if (desired.contains(iterator->name)) continue;
                RestoreProperty(element, *iterator);
                node.saved.erase(iterator);
            }
            if (auto animation = desired.find(L"DockAnimation"); animation != desired.end()) {
                auto separator = animation->second.find(L',');
                if (separator == std::wstring::npos) throw hresult_invalid_argument();
                auto percent = std::stoi(animation->second.substr(0, separator));
                auto duration = std::stoi(animation->second.substr(separator + 1));
                if (percent < 105 || percent > 160 || duration < 50 || duration > 500) throw hresult_invalid_argument();
                auto factor = percent / 100.0;
                if (!node.dock || !node.dock->Matches(factor, duration)) {
                    node.dock.reset();
                    node.dock = std::make_unique<TaskbarDockAnimation>(element, factor, duration);
                    InterlockedIncrement(&session_.Get()->changed);
                }
                desired.erase(animation);
            } else node.dock.reset();
            if (auto width = desired.find(L"IconColumnWidth"); width != desired.end()) {
                auto pixels = std::stod(width->second);
                if (!std::isfinite(pixels) || pixels < 24 || pixels > 156) throw hresult_invalid_argument();
                auto grid = element.as<Grid>();
                if (!node.iconColumn && grid.ColumnDefinitions().Size() > 0) {
                    int index = 0;
                    for (auto const& child : grid.Children()) {
                        if (auto item = child.try_as<FrameworkElement>(); item && item.Name() == L"Icon") {
                            index = Grid::GetColumn(item);
                            break;
                        }
                    }
                    node.iconColumn = grid.ColumnDefinitions().GetAt(static_cast<uint32_t>(index));
                    node.originalIconColumnWidth = node.iconColumn.ReadLocalValue(ColumnDefinition::WidthProperty());
                }
                node.iconColumnWidth = pixels;
                if (node.iconColumn) node.iconColumn.Width(GridLength{pixels, GridUnitType::Pixel});
                desired.erase(width);
            } else RestoreIconColumn(node);
            if (node.type == L"Taskbar.TaskbarFrame" && desired.contains(L"Height")) {
                auto host = GetAncestor(HostWindow(node), GA_ROOT);
                wchar_t name[64]{};
                GetClassNameW(host, name, static_cast<int>(std::size(name)));
                if (wcscmp(name, L"Shell_TrayWnd") != 0 && wcscmp(name, L"Shell_SecondaryTrayWnd") != 0)
                    throw hresult_error(E_INVALIDARG);
                if (!node.geometry) node.geometry = std::make_unique<TaskbarGeometry>();
                if (!node.geometry->Update(host, std::stoi(desired.at(L"Height")))) {
                    auto error = GetLastError();
                    throw hresult_error(HRESULT_FROM_WIN32(error), hstring(node.geometry->DescribeDiagnostic()));
                }
            }
            for (auto const& [name, text] : desired) {
                auto saved = std::find_if(node.saved.begin(), node.saved.end(), [&](auto const& entry) { return entry.name == name; });
                if (saved != node.saved.end() && saved->desired == text) continue;
                auto [property, value] = Property(element, name, text);
                if (!property) { RecordError(E_NOTIMPL); continue; }
                if (saved == node.saved.end()) {
                    auto expression = element.GetBindingExpression(property);
                    node.saved.push_back({property, element.ReadLocalValue(property), expression ? expression.ParentBinding() : nullptr, name, text});
                } else saved->desired = text;
                element.SetValue(property, value);
                InterlockedIncrement(&session_.Get()->changed);
            }
            if (node.type == L"Taskbar.TaskbarFrame" && !desired.contains(L"Height") && node.geometry) {
                if (!node.geometry->Restore()) throw_last_error();
                node.geometry.reset();
            }
        } catch (hresult_error const& error) { RecordError(error.code(), std::wstring(error.message())); }
        catch (...) { RecordError(to_hresult()); }
    }

    void WriteSnapshot() {
        std::lock_guard serializationLock(snapshotMutex_);
        JsonArray array;
        std::size_t serializedCharacters = 2;
        {
            std::lock_guard lock(mutex_);
            for (auto const& [handle, entry] : snapshot_) {
                JsonObject node;
                node.SetNamedValue(L"handle", JsonValue::CreateNumberValue(static_cast<double>(handle)));
                node.SetNamedValue(L"parent", JsonValue::CreateNumberValue(static_cast<double>(entry.parent)));
                node.SetNamedValue(L"type", JsonValue::CreateStringValue(entry.type));
                node.SetNamedValue(L"name", JsonValue::CreateStringValue(entry.name));
                node.SetNamedValue(L"automationId", JsonValue::CreateStringValue(entry.automationId));
                node.SetNamedValue(L"accessibleName", JsonValue::CreateStringValue(entry.accessibleName));
                node.SetNamedValue(L"width", JsonValue::CreateNumberValue(entry.width));
                node.SetNamedValue(L"height", JsonValue::CreateNumberValue(entry.height));
                node.SetNamedValue(L"opacity", JsonValue::CreateNumberValue(entry.opacity));
                node.SetNamedValue(L"visibility", JsonValue::CreateStringValue(entry.visibility));
                node.SetNamedValue(L"text", JsonValue::CreateStringValue(entry.text));
                node.SetNamedValue(L"fill", JsonValue::CreateStringValue(entry.fill));
                node.SetNamedValue(L"screenLeft", JsonValue::CreateNumberValue(entry.screenBounds.X));
                node.SetNamedValue(L"screenTop", JsonValue::CreateNumberValue(entry.screenBounds.Y));
                node.SetNamedValue(L"screenWidth", JsonValue::CreateNumberValue(entry.screenBounds.Width));
                node.SetNamedValue(L"screenHeight", JsonValue::CreateNumberValue(entry.screenBounds.Height));
                node.SetNamedValue(L"hostHeight", JsonValue::CreateNumberValue(entry.hostHeight));
                node.SetNamedValue(L"dockAnimation", JsonValue::CreateBooleanValue(entry.dockAnimation));
                node.SetNamedValue(L"dockScale", JsonValue::CreateNumberValue(entry.dockScale));
                auto characters = node.Stringify().size() + 1;
                if (serializedCharacters + characters >= (MappingBytes - SnapshotOffset) / sizeof(wchar_t)) break;
                serializedCharacters += characters;
                array.Append(node);
                if (array.Size() >= 4096) break;
            }
            session_.Get()->elements = static_cast<LONG>(snapshot_.size());
        }
        session_.Snapshot(std::wstring(array.Stringify()));
    }

public:
    HRESULT STDMETHODCALLTYPE OnElementStateChanged(InstanceHandle, VisualElementState, LPCWSTR) noexcept override { return S_OK; }

    HRESULT STDMETHODCALLTYPE SetSite(IUnknown* site) noexcept override {
        try {
            if (!site) return S_OK;
            std::lock_guard lock(lifetime_);
            if (session_.Get()) return HRESULT_FROM_WIN32(ERROR_BUSY);
            stopped_ = false;
            site_.copy_from(site);
            diagnostics_ = site_.as<IXamlDiagnostics>();
            service_ = site_.as<IVisualTreeService>();
            BSTR data{};
            check_hresult(diagnostics_->GetInitializationData(&data));
            std::wstring name(data, SysStringLen(data));
            SysFreeString(data);
            if (!session_.Open(name)) return E_ACCESSDENIED;
            session_.Get()->processId = GetCurrentProcessId();
            InterlockedIncrement(&session_.Get()->attached);
            auto self = get_strong();
            std::thread([self] {
                try {
                    init_apartment(apartment_type::multi_threaded);
                    check_hresult(self->service_->AdviseVisualTreeChange(self.get()));
                    while (self->session_.Alive()) {
                        self->WriteSnapshot();
                        Sleep(PollMilliseconds);
                    }
                    { std::lock_guard lifetimeLock(self->lifetime_); self->stopped_ = true; }
                    if (FAILED(self->service_->UnadviseVisualTreeChange(self.get()))) self->RecordError();
                    std::vector<HWND> windows;
                    { std::lock_guard lock(self->mutex_);
                        for (auto const& [thread, context] : self->contexts_) { (void)thread; windows.push_back(context->window); }
                    }
                    auto closed = true;
                    for (auto window : windows) {
                        DWORD_PTR ignored{};
                        if (!SendMessageTimeoutW(window, WM_APP + 2, 0, 0, SMTO_ABORTIFHUNG, 2000, &ignored)) closed = false;
                    }
                    if (closed) {
                        std::lock_guard lifetimeLock(self->lifetime_);
                        { std::lock_guard stateLock(self->mutex_);
                            self->contexts_.clear(); self->snapshot_.clear(); self->rootWindows_.clear();
                        }
                        self->service_ = nullptr; self->diagnostics_ = nullptr; self->site_ = nullptr;
                        InterlockedExchange(&self->session_.Get()->closed, 1);
                        self->session_.Close();
                    }
                } catch (...) { self->RecordError(); }
            }).detach();
            return S_OK;
        } catch (...) { return to_hresult(); }
    }

    HRESULT STDMETHODCALLTYPE GetSite(REFIID iid, void** result) noexcept override {
        return site_ ? site_->QueryInterface(iid, result) : E_NOINTERFACE;
    }

    HRESULT STDMETHODCALLTYPE OnVisualTreeChange(ParentChildRelation relation, VisualElement element, VisualMutationType mutation) noexcept override {
        try {
            std::lock_guard lifetimeLock(lifetime_);
            if (stopped_) return S_OK;
            if (mutation == VisualMutationType::Remove) {
                std::lock_guard lock(mutex_);
                for (auto const& [thread, context] : contexts_) {
                    (void)thread;
                    PostMessageW(context->window, WM_APP + 1, static_cast<WPARAM>(element.Handle), 0);
                }
                snapshot_.erase(element.Handle);
                rootWindows_.erase(element.Handle);
            } else {
                com_ptr<::IInspectable> inspectable;
                check_hresult(diagnostics_->GetIInspectableFromHandle(element.Handle, inspectable.put()));
                if (auto source = inspectable.try_as<Hosting::DesktopWindowXamlSource>()) {
                    HWND window{};
                    if (SUCCEEDED(source.as<IDesktopWindowXamlSourceNative>()->get_WindowHandle(&window))) {
                        std::lock_guard lock(mutex_);
                        rootWindows_[element.Handle] = window;
                    }
                }
                auto framework = inspectable.try_as<FrameworkElement>();
                if (!framework) {
                    std::lock_guard lock(mutex_);
                    auto& entry = snapshot_[element.Handle];
                    entry.parent = relation.Parent;
                    entry.type = element.Type ? element.Type : L"";
                    entry.name = element.Name ? element.Name : L"";
                    return S_OK;
                }
                auto self = get_strong();
                std::wstring type = element.Type ? element.Type : L"";
                std::wstring name = element.Name ? element.Name : L"";
                auto track = [self, handle = element.Handle, parent = relation.Parent, type, name] {
                    try { self->Track(handle, parent, type, name); }
                    catch (...) { self->RecordError(); }
                };
                auto dispatcher = framework.Dispatcher();
                if (dispatcher.HasThreadAccess()) track();
                else dispatcher.RunAsync(Windows::UI::Core::CoreDispatcherPriority::Normal, track);
            }
            return S_OK;
        } catch (...) { RecordError(); return S_OK; }
    }

    void Track(InstanceHandle handle, InstanceHandle parent, std::wstring const& type, std::wstring const& name) {
        std::lock_guard lifetimeLock(lifetime_);
        if (stopped_) return;
        auto& context = Context();
        if (context.nodes.size() >= 8192) return;
        com_ptr<::IInspectable> inspectable;
        check_hresult(diagnostics_->GetIInspectableFromHandle(handle, inspectable.put()));
        auto framework = inspectable.try_as<FrameworkElement>();
        if (!framework) return;
        Node node;
        node.handle = handle;
        node.parent = parent;
        node.type = type;
        node.name = name.empty() ? std::wstring(framework.Name()) : name;
        node.element = make_weak(framework);
        auto [iterator, inserted] = context.nodes.try_emplace(handle, std::move(node));
        if (inserted) {
            UpdateSnapshot(iterator->second);
            Apply(context, iterator->second);
        }
    }

    void UpdateSnapshot(Node const& node) {
        auto element = node.element.get();
        if (!element) return;
        SnapshotNode entry;
        entry.parent = node.parent;
        entry.type = node.type;
        entry.name = node.name;
        entry.automationId = Automation::AutomationProperties::GetAutomationId(element);
        entry.accessibleName = Automation::AutomationProperties::GetName(element);
        entry.width = element.ActualWidth();
        entry.height = element.ActualHeight();
        RECT hostBounds{};
        if (GetWindowRect(HostWindow(node), &hostBounds)) entry.hostHeight = hostBounds.bottom - hostBounds.top;
        entry.dockAnimation = node.dock != nullptr;
        entry.dockScale = node.dock ? node.dock->Scale() : 1.0;
        entry.opacity = element.Opacity();
        entry.visibility = element.Visibility() == Visibility::Visible ? L"Visible" : L"Collapsed";
        {
            try {
                auto host = HostWindow(node);
                RECT bounds{};
                if (host && GetWindowRect(host, &bounds)) {
                    {
                        auto relative = element.TransformToVisual(nullptr).TransformBounds(
                            Rect{0, 0, static_cast<float>(entry.width), static_cast<float>(entry.height)});
                        auto scale = static_cast<float>(GetDpiForWindow(host)) / 96.0f;
                        entry.screenBounds = Rect{bounds.left + relative.X * scale, bounds.top + relative.Y * scale,
                            relative.Width * scale, relative.Height * scale};
                    }
                }
            } catch (...) { }
        }
        if (auto text = element.try_as<TextBlock>()) entry.text = text.Text();
        if (auto shape = element.try_as<Shapes::Shape>()) {
            if (auto brush = shape.Fill().try_as<SolidColorBrush>()) {
                auto color = brush.Color();
                wchar_t value[10]{};
                swprintf_s(value, L"#%02X%02X%02X%02X", color.A, color.R, color.G, color.B);
                entry.fill = value;
            }
        }
        std::lock_guard lock(mutex_);
        snapshot_[node.handle] = std::move(entry);
    }

    HWND HostWindow(Node const& node) {
        std::lock_guard lock(mutex_);
        auto ancestor = node.parent;
        for (unsigned int depth = 0; ancestor && depth < 64; ++depth) {
            if (auto root = rootWindows_.find(ancestor); root != rootWindows_.end()) return root->second;
            auto parent = snapshot_.find(ancestor);
            if (parent == snapshot_.end()) break;
            ancestor = parent->second.parent;
        }
        return nullptr;
    }

    void Poll(UiContext& context) noexcept {
        try {
            std::lock_guard lifetimeLock(lifetime_);
            if (stopped_) return;
            std::wstring json;
            LONG revision{};
            if (!session_.Read(json, revision)) {
                if (session_.Alive()) return;
                for (auto& [handle, node] : context.nodes) { (void)handle; Restore(node); }
                context.configuration = nullptr;
                context.revision = -1;
                return;
            }
            if (revision != context.revision) {
                auto configuration = JsonObject::Parse(json);
                context.configuration = configuration;
                context.revision = revision;
                for (auto& [handle, node] : context.nodes) { (void)handle; Apply(context, node); }
                InterlockedExchange(&session_.Get()->appliedRevision, revision);
            }
            for (auto const& [handle, node] : context.nodes) {
                (void)handle;
                if (node.geometry && !node.geometry->Refresh())
                    RecordError(HRESULT_FROM_WIN32(GetLastError()), node.geometry->DescribeDiagnostic());
                // Explorer refreshes the fixed icon column and panel width
                // when button states change. Preserve the active compact size.
                if (node.iconColumn) {
                    auto width = GridLength{node.iconColumnWidth, GridUnitType::Pixel};
                    auto current = node.iconColumn.Width();
                    if (current.Value != width.Value || current.GridUnitType != width.GridUnitType)
                        node.iconColumn.Width(width);
                    if (auto element = node.element.get()) {
                        for (auto const& saved : node.saved) {
                            if (saved.name == L"Width" && element.Width() != std::stod(saved.desired))
                                element.Width(std::stod(saved.desired));
                        }
                    }
                }
                UpdateSnapshot(node);
            }
            WriteSnapshot();
        } catch (...) { RecordError(); }
    }
};

struct Factory : implements<Factory, IClassFactory> {
    HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer, REFIID iid, void** result) noexcept override {
        if (outer) return CLASS_E_NOAGGREGATION;
        try { return make_self<Tap>()->QueryInterface(iid, result); }
        catch (...) { return to_hresult(); }
    }
    HRESULT STDMETHODCALLTYPE LockServer(BOOL) noexcept override { return S_OK; }
};
}

STDAPI DllGetClassObject(REFCLSID clsid, REFIID iid, void** result) {
    if (clsid != winforge::shell::TapClassId) return CLASS_E_CLASSNOTAVAILABLE;
    try { return winrt::make_self<winforge::shell::Factory>()->QueryInterface(iid, result); }
    catch (...) { return winrt::to_hresult(); }
}
STDAPI DllCanUnloadNow() { return S_FALSE; }
