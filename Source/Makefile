KSPDIR		:= ${HOME}/ksp/KSP_linux
MANAGED		:= ${KSPDIR}/KSP_Data/Managed
GAMEDATA	:= ${KSPDIR}/GameData
pWingsGAMEDATA  := ${GAMEDATA}/ProceduralDynamics
PLUGINDIR	:= ${pWingsGAMEDATA}/Plugins

TARGETS		:= pWings.dll

pWings_FILES := \
	FlapToggler.cs	\
	WingManipulator.cs	\
	stupidMathNonsense.cs	\
	Properties/AssemblyInfoPWings.cs	\
	$e

RESGEN2		:= resgen2
GMCS		:= gmcs
GMCSFLAGS	:= -optimize -warnaserror
GIT			:= git
TAR			:= tar
ZIP			:= zip

all: ${TARGETS}
#all: version ${TARGETS}

.PHONY: version
version:
	@./git-version.sh

info:
	@echo "pWings Build Information"
	@echo "    resgen2:    ${RESGEN2}"
	@echo "    gmcs:       ${GMCS}"
	@echo "    gmcs flags: ${GMCSFLAGS}"
	@echo "    git:        ${GIT}"
	@echo "    tar:        ${TAR}"
	@echo "    zip:        ${ZIP}"
	@echo "    KSP Data:   ${KSPDIR}"

pWings.dll: ${pWings_FILES}
	${GMCS} ${GMCSFLAGS} -t:library -lib:${MANAGED} \
		-r:Assembly-CSharp,Assembly-CSharp-firstpass,UnityEngine \
		-out:$@ $^

clean:
	rm -f ${TARGETS}

install: all
	mkdir -p ${PLUGINDIR}
	cp ${TARGETS} ${PLUGINDIR}

.PHONY: all clean install
