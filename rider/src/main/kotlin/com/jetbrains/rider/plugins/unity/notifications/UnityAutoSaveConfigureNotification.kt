package com.jetbrains.rider.plugins.unity.notifications

import com.intellij.ide.GeneralSettings
import com.intellij.ide.util.PropertiesComponent
import com.intellij.notification.NotificationGroup
import com.intellij.openapi.editor.Editor
import com.intellij.openapi.editor.EditorFactory
import com.intellij.openapi.editor.event.DocumentEvent
import com.intellij.openapi.editor.event.DocumentListener
import com.intellij.openapi.fileEditor.FileEditorManager
import com.intellij.openapi.fileEditor.impl.text.TextEditorProvider
import com.intellij.openapi.project.Project
import com.intellij.openapi.util.Key
import com.intellij.ui.EditorNotificationPanel
import com.intellij.ui.LightColors
import com.jetbrains.rd.util.lifetime.Lifetime
import com.jetbrains.rd.util.lifetime.isAlive
import com.jetbrains.rd.util.lifetime.onTermination
import com.jetbrains.rd.util.reactive.*
import com.jetbrains.rdclient.util.idea.LifetimedProjectComponent
import com.jetbrains.rider.UnityProjectDiscoverer
import com.jetbrains.rider.debugger.settings.DotNetDebuggerSettings
import com.jetbrains.rider.document.getFirstEditor
import com.jetbrains.rider.model.EditorState
import com.jetbrains.rider.model.ScriptCompilationDuringPlay
import com.jetbrains.rider.plugins.unity.UnityHost
import com.jetbrains.rider.projectView.SolutionLifecycleHost
import com.jetbrains.rider.util.idea.application

class UnityAutoSaveConfigureNotification (project: Project, private val unityProjectDiscoverer: UnityProjectDiscoverer,
                                          private val unityHost: UnityHost, solutionLifecycleHost: SolutionLifecycleHost) : LifetimedProjectComponent(project){

    private val propertiesComponent: PropertiesComponent = PropertiesComponent.getInstance()
    private var lifetimeDefinition = componentLifetime.createNested()
    private val KEY = Key.create<EditorNotificationPanel>("PromoteAutoSave")

    companion object {
        private const val settingName = "do_not_show_unity_auto_save_notification"
    }

    init {
        solutionLifecycleHost.isBackendLoaded.whenTrue(componentLifetime) {
            if (!propertiesComponent.getBoolean(settingName) && unityProjectDiscoverer.isUnityProject) {

                val eventMulticaster = EditorFactory.getInstance().eventMulticaster
                val documentListener: DocumentListener = object : DocumentListener {
                    override fun documentChanged(event: DocumentEvent) {
                        if (unityHost.model.editorState.valueOrDefault(EditorState.Disconnected) != EditorState.ConnectedPlay)
                            return

                        if (!unityHost.model.scriptCompilationDuringPlay.hasValue)
                            return

                        if (unityHost.model.scriptCompilationDuringPlay.valueOrThrow != ScriptCompilationDuringPlay.RecompileAndContinuePlaying)
                            return

                        if (!lifetimeDefinition.isAlive)
                            return

                        val generalSettings = GeneralSettings.getInstance()
                        if (generalSettings.isAutoSaveIfInactive || generalSettings.isSaveOnFrameDeactivation) {

                            val editor = event.document.getFirstEditor(project) ?: return
                            showNotification(lifetimeDefinition.lifetime, editor)
                        }
                    }
                }

                eventMulticaster.addDocumentListener(documentListener)
                lifetimeDefinition.lifetime.onTermination {
                    eventMulticaster.removeDocumentListener(documentListener)
                }
            }
        }
    }

    fun showNotification(lifetime: Lifetime, editor: Editor) {
        application.assertIsDispatchThread()

        if (!lifetime.isAlive) return
        if (!DotNetDebuggerSettings.instance.shouldPromoteEditAndContinue) return
        val project = editor.project ?: return

        val textEditor = TextEditorProvider.getInstance().getTextEditor(editor)
        if (textEditor.getUserData(KEY) != null) return

        val panel = EditorNotificationPanel(LightColors.RED)
        panel.setText("You are modifying a script while Unity Editor is being in Play Mode. This can lead to a loss of the state in your running game.")

        panel.createActionLabel("Configure Unity Editor") {
            unityHost.model.showPreferences.fire()
            lifetimeDefinition.terminate()
        }

        panel.createActionLabel("Don't show again") {
            propertiesComponent.setValue(settingName, true)
            lifetimeDefinition.terminate()
        }

        panel.createActionLabel("X") {
            lifetimeDefinition.terminate()
        }

        lifetimeDefinition.onTermination {
            if (textEditor.getUserData(KEY) != null) {
                FileEditorManager.getInstance(project).removeTopComponent(textEditor, panel)
                textEditor.putUserData(KEY, null)
            }
        }

        textEditor.putUserData(KEY, panel)
        FileEditorManager.getInstance(project).addTopComponent(textEditor, panel)
    }
}