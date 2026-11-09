use super::*;

impl UnityRenderer {
    /// Prepare real GPU programs and pipelines for the current attachment configuration.
    /// As with an ordinary draw, a different later render state can require another pipeline.
    pub fn shader_warmup(
        &mut self,
        request: unbound_runtime::render_protocol::ShaderWarmupRequest,
    ) -> Result<String, String> {
        if request.stats {
            let (programs, pipelines) = self.compiled.prepared_counts(&request.shader);
            return Ok(
                serde_json::json!({"programs": programs, "pipelines": pipelines}).to_string(),
            );
        }
        let material = owned::Material {
            shader: request.shader,
            render_queue: -1,
            keywords: request.keywords,
            ..Default::default()
        };
        let sources = if request.all_variants {
            self.resolver.warmup_sources(&material)?
        } else {
            vec![self.resolver.resolve(&material)]
        };
        let target = match self.state.target.clone() {
            Some(target) => target,
            None => self.screen_target(self.color_space),
        };
        let layout = self.pipelines.texture_layout.clone();
        for resolved in sources {
            let passes: Vec<usize> = if request.all_variants {
                (0..resolved.all.len()).collect()
            } else {
                request.passes.clone()
            };
            for index in passes {
                let pass = match resolved
                    .all
                    .get(index)
                    .ok_or("warm-up pass is out of range")?
                {
                    Ok(pass) => pass,
                    Err(why) => {
                        tracing::warn!(
                            "shader `{}` pass {index} cannot draw during warm-up: {why}",
                            resolved.shader
                        );
                        continue;
                    }
                };
                if pass.grab.is_some() {
                    continue;
                }
                let variants: Box<dyn Iterator<Item = Vec<String>>> = if request.all_variants {
                    Box::new(
                        self.compiled
                            .warmup_variants(&pass.program, self.color_space == 1)?,
                    )
                } else {
                    Box::new(std::iter::once(material.keywords.clone()))
                };
                for variant in variants {
                    // Mesh calls use replay; raw procedural shaders can also use the literal keyword.
                    // A raw Unity instance buffer is not needed by replay, so its unavailable form
                    // must not make preparation of a supported mesh variant fail or log a warning.
                    let instancing = variant.iter().any(|k| k == "INSTANCING_ON");
                    for replay in [false, true] {
                        if replay && !instancing {
                            continue;
                        }
                        let mut keywords = variant.clone();
                        if replay {
                            keywords.push(unbound_shaders::MESH_INSTANCE_REPLAY.into());
                        }
                        let (id, errored) = if instancing && !replay {
                            let Some(id) = self.compiled.variant(
                                &self.device,
                                &resolved.shader,
                                &pass.program,
                                &keywords,
                                self.color_space == 1,
                                &layout,
                                compiled::VariantUse::OptionalWarmup,
                            ) else {
                                continue;
                            };
                            (id, false)
                        } else {
                            let (id, errored) = self.variant_or_error(
                                &resolved.shader,
                                &pass.program,
                                &keywords,
                                self.color_space == 1,
                                &layout,
                            );
                            (
                                id.ok_or_else(|| {
                                    format!(
                                        "shader `{}` pass {index} could not be warmed up",
                                        resolved.shader
                                    )
                                })?,
                                errored,
                            )
                        };
                        // Program preparation does not require this attachment configuration.
                        // As with other later target changes, an incompatible scalar type can
                        // require a pipeline on the first draw into the appropriate target.
                        if !self
                            .compiled
                            .accepts_target(id, target.format, &target.extra_formats())
                        {
                            continue;
                        }
                        for topology in [
                            resources::Topology::Triangles,
                            resources::Topology::Lines,
                            resources::Topology::LineStrip,
                            resources::Topology::Points,
                        ] {
                            for mirrored in [false, true] {
                                let (state, _) = self.draw_state(
                                    if errored {
                                        Default::default()
                                    } else {
                                        pass.state
                                    },
                                    pass.stencil_ref,
                                    mirrored,
                                );
                                self.compiled.pipeline(
                                    &self.device,
                                    id,
                                    PipelineKey {
                                        state,
                                        topology,
                                        format: target.format,
                                        depth: target.depth.is_some(),
                                        samples: target.samples,
                                        depth_bias: self.state.depth_bias.map(f32::to_bits),
                                        extra: target.extra_formats(),
                                    },
                                )?;
                            }
                        }
                    }
                }
            }
        }
        Ok("{}".into())
    }

    pub(super) fn draw_state(
        &self,
        mut state: crate::materials::RenderState,
        mut stencil_ref: u32,
        mirrored: bool,
    ) -> (crate::materials::RenderState, u32) {
        if state.gui_ztest {
            state.ztest = if self.state.overlay { 8 } else { 4 };
        }
        if let Some(o) = &self.state.state_override {
            stencil_ref = apply_override(&mut state, stencil_ref, o);
        }
        state.wireframe = self.state.wireframe;
        if self.state.invert_culling != mirrored {
            state.cull = match state.cull {
                1 => 2,
                2 => 1,
                c => c,
            };
        }
        (state, stencil_ref)
    }
}
