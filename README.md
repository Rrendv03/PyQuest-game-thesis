# PyQuest: An Adaptive Educational Game for Python Programming

## Project Introduction
PyQuest is an academic thesis project developed as an adaptive educational game within the Unity engine. The application is designed to personalize the learning process for introductory Python programming concepts, dynamically adjusting educational content to align with student performance and learning pacing. This repository contains the core source code, configuration manifests, and structural assets required to build and execute the project. It would be created within a 6-7 month timeline, this will serve as an undergraduate thesis project

---

## Technical Prerequisites

### Required Software
To run, edit, or evaluate this project, you must install three software components:
* **Unity Hub:** The official management application used to handle different Unity Editor versions and project instances.
* **Unity Editor Version:** `2022.3.18f1 . You must use a version within the 2022.3 release cycle to ensure complete API compatibility and prevent compilation script failures.
* **Microsoft Visual Studio 2022.17.14.29:** The designated integrated development environment (IDE) used for writing, debugging, and compiling the C# scripts for the game logic.
* **Blender 5.1:** This is for the asset building of the game's environment.
* **Photoshop/GIMP:** This is for texturing and UI designing for the game's assets and design. 


### Repository Architecture (What You Are Downloading)
When you clone or download this branch, your local machine will receive only the essential structural directories:
* `Assets/`: Contains all custom C# scripts, game scenes, user interface elements, and visual assets.
* `Packages/`: Contains the dependency manifests telling the engine which official libraries to install.
* `ProjectSettings/`: Contains the global game engine configurations, input maps, and physics definitions.
* `.vsconfig`: Configures Visual Studio 2022 automatically with the exact workloads needed for this project.

*Note: The heavy database files (`Library/`) are intentionally excluded from GitHub. Unity will recreate them automatically on your machine during the initial startup sequence.*

---

## Detailed Step-by-Step Installation Guide

### Step 1: Install Unity Hub
* Open your web browser and navigate to the official Unity download page.
* Download the installer for **Unity Hub** corresponding to your operating system (Windows or macOS).
* Run the downloaded installation wizard, accept the standard terms of service, and complete the installation process.
* Launch Unity Hub and sign in with a free Unity ID account.

### Step 2: Install the Exact Unity Editor Version
* Inside the Unity Hub application, look at the left-hand sidebar and click on the **Installs** tab.
* Click the **Install Editor** button located in the top-right corner.
* Locate the **Official releases** section and look for the version label starting with **2022.3** (marked with an LTS tag).
* Click **Install**. When the module selection window appears, you can uncheck the default Visual Studio option if you prefer to install it separately, then click **Continue** to begin the download.

### Step 3: Install Microsoft Visual Studio 2022
* Navigate to the official Microsoft Visual Studio download page in your browser.
* Download the installer for **Visual Studio 2022 Community Edition** (which is free for students and academic research).
* Run the Visual Studio Installer program.
* When the workload selection screen appears, scroll down to the **Desktop & Mobile** section and check the box for **Game development with Unity**. This option installs the necessary C# compilers and tools required by the engine.
* Click the **Install** button in the bottom right corner and wait for the download and installation to finish completely.

### Step 4: Download the Project Files from GitHub
* Scroll to the top of this GitHub repository page.
* Click the green button labeled **Code**.
* If you do not use Git command line tools, click **Download ZIP** from the dropdown menu.
* Once the download completes, locate the ZIP file in your file explorer and extract its contents into a dedicated workspace directory on your local storage drive.

### Step 5: Import and Open the Project in Unity Hub
* Launch the **Unity Hub** application.
* Navigate to the **Projects** tab on the left sidebar.
* Click the **Open** dropdown button in the top-right corner and select **Add project from disk**.
* A file browser window will appear. Navigate through your directories and click on the root folder of the extracted project (the folder that contains the `Assets` and `ProjectSettings` directories).
* Click **Add Project**. The project name will now appear permanently inside your Unity Hub list.
* Click on the project name within the list to launch the Unity Editor environment.

### Step 6: Link Unity to Visual Studio 2022
* Once the Unity Editor completely loads up, look at the top menu bar and click on **Edit**, then select **Preferences** (on macOS, click **Unity** then **Preferences**).
* In the Preferences window that pops up, click on the **External Tools** tab on the left side.
* Look for the first option labeled **External Script Editor**.
* Click the dropdown menu next to it and select **Microsoft Visual Studio 2022**. If it does not appear in the list, click **Browse** and navigate to where you installed it on your hard drive.
* Close the Preferences window. Now, whenever you double-click a C# script inside the `Assets/` folder, it will automatically open in Visual Studio 2022 with proper autocomplete and error checking enabled.

### Step 7: Wait for Initial Asset Generation
* When you open the project for the first time, a loading progress bar will appear stating that Unity is importing assets and generating package configurations.
* This reconstruction process can take several minutes because the engine is completely rebuilding the missing local cache database from your raw source files. Do not close the window or terminate the process.
* Once the loading sequence concludes, the Unity Editor interface will open completely, displaying the project workspace. Double-click on the initial scene file located within the `Assets/Scenes/` folder to load the game environment.

---

## AI Assistant Copilot Setup Prompt

If you want an artificial intelligence assistant to guide you through this configuration process interactively, copy the block below and paste it directly into your AI chat interface:

```text
Act as a technical support assistant for an academic game development project called PyQuest. I need to deploy this project locally. Review the repository architecture, requirements, and setup processes detailed in the project documentation. Provide a brief overview of the required software ecosystem, then guide me through the configuration of Unity Hub, Unity Editor 2022.3.x LTS, Microsoft Visual Studio 2022, and the project importing process. Present the setup rules sequentially, and explicitly wait for me to confirm that I have finished the current step before you provide the technical guidelines for the next phase.