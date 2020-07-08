package unityExplorer

import com.jetbrains.rider.test.annotations.TestEnvironment
import com.jetbrains.rider.test.base.ProjectModelBaseTest
import com.jetbrains.rider.test.scriptingApi.TemplateType
import com.jetbrains.rider.test.scriptingApi.testProjectModel
import org.testng.Assert
import org.testng.annotations.Test
import java.nio.file.Paths

class UnityProjectModelViewExtensionsTest : ProjectModelBaseTest() {
    override fun getSolutionDirectoryName() = "com.unity.ide.rider"
    override val persistCaches: Boolean
        get() = true

    @Test
    @TestEnvironment(solution = "UnityProjectModelViewExtensionsTest")
    fun testAddNewItem() {
        testProjectModel(testGoldFile, project, false) {
            //dump("Init", project, activeSolutionDirectory) {}
            dump("Add files and classes", project, activeSolutionDirectory) {
                // add file to Assets\AsmdefResponse\NewDirectory1 is ambig between 2 asmdef projects
                // add file to Assets\NewDirectory1 is ambig between predefined projects and asmdef
                // goes to Editor project

                addNewItem(project, arrayOf("Assets", "AsmdefResponse", "NewDirectory1"), TemplateType.CLASS, "AsmdefClass.cs")
                addNewItem(project, arrayOf("Assets", "NewDirectory1"), TemplateType.CLASS, "MainClass.cs")
                addNewItem(project, arrayOf("Assets", "Scripts", "Editor", "NewDirectory1"), TemplateType.CLASS, "EditorClass.cs")
            }
        }
    }

    @Test
    @TestEnvironment(solution = "UnityProjectModelViewExtensionsTest")
    fun testRenameFile() {
        testProjectModel(testGoldFile, project, false) {
            dump("Rename file", project, activeSolutionDirectory) {
                val metaFileContent = Paths.get(project.basePath!!).resolve("Assets").resolve("AsmdefResponse").resolve("NewBehaviourScript.cs.meta").toFile().readText()

                doActionAndWait(project, {
                    renameItem(project, arrayOf("Assets", "AsmdefResponse", "NewBehaviourScript.cs"), "NewBehaviourScript_renamed.cs")
                },true)

                val metaFile = Paths.get(project.basePath!!).resolve("Assets").resolve("AsmdefResponse").resolve("NewBehaviourScript_renamed.cs.meta").toFile()
                Assert.assertTrue(metaFile.exists(), "meta file $metaFile doesn't exist.")
                Assert.assertEquals(metaFileContent, metaFile.readText())
            }
        }
    }

    @Test
    @TestEnvironment(solution = "UnityProjectModelViewExtensionsTest")
    fun testRenameFolder() {
        testProjectModel(testGoldFile, project, false) {
            dump("Rename folder", project, activeSolutionDirectory) {
                val metaFileContent = Paths.get(project.basePath!!).resolve("Assets").resolve("Dir1.meta").toFile().readText()

                doActionAndWait(project, {
                    renameItem(project, arrayOf("Assets", "Dir1"), "Dir1_renamed")
                },true)

                val metaFile = Paths.get(project.basePath!!).resolve("Assets").resolve("Dir1_renamed.meta").toFile()
                Assert.assertTrue(metaFile.exists(), "meta file $metaFile doesn't exist.")
                Assert.assertEquals(metaFileContent, metaFile.readText())
            }
        }
    }

    @Test
    @TestEnvironment(solution = "UnityProjectModelViewExtensionsTest")
    fun testDeleteFile() {
        testProjectModel(testGoldFile, project, false) {
            dump("Rename folder", project, activeSolutionDirectory) {
                val metaFile = Paths.get(project.basePath!!).resolve("Assets").resolve("AsmdefResponse").resolve("NewBehaviourScript.cs.meta").toFile()
                Assert.assertTrue(metaFile.exists(), "We expect meta file exists.")

                doActionAndWait(project, {
                    deleteElement(project, arrayOf("Assets", "AsmdefResponse", "NewBehaviourScript.cs"))
                },true)

                Assert.assertFalse(metaFile.exists(), "We expect meta file removed.")
            }
        }
    }

    @Test
    @TestEnvironment(solution = "RiderMoveFile") // RIDER-41182
    fun testMoveFile() {
        val action = {
            // in Rider move the script file "MyScript" into "SomeFolder"
            // meta file should be moved together with script

            cutItem2(project, arrayOf("Assets", "MyScript.cs"))
            pasteItem2(project, arrayOf("Assets", "SomeFolder"), "MyScript.cs")
        }
        val metaFileContent = Paths.get(project.basePath!!).resolve("Assets").resolve("MyScript.cs.meta").toFile().readText()

        doActionAndWait(project, action,true)

        val metaFile = Paths.get(project.basePath!!).resolve("Assets").resolve("SomeFolder").resolve("MyScript.cs.meta").toFile()
        Assert.assertTrue(metaFile.exists(), "meta file $metaFile doesn't exist.")
        Assert.assertEquals(metaFileContent, metaFile.readText())
    }
}
